#import <Foundation/Foundation.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <CoreGraphics/CoreGraphics.h>
#import <CoreVideo/CoreVideo.h>

// All stream and pixel-buffer state is confined to this capture's serial queue.
@interface EOPWindowCapture : NSObject <SCStreamOutput, SCStreamDelegate> {
@public
    dispatch_queue_t queue;
    SCStream *stream;
    CVPixelBufferRef frame;
    uint64_t sequence;
    NSString *errorMessage;
    BOOL stopped;
    BOOL starting;
}
- (void)startWindow:(uint32_t)windowID width:(int)width height:(int)height;
- (void)stop;
@end

@implementation EOPWindowCapture
- (instancetype)init {
    if ((self = [super init])) {
        queue = dispatch_queue_create("com.eveopreview.capture", DISPATCH_QUEUE_SERIAL);
    }
    return self;
}

- (void)startWindow:(uint32_t)windowID width:(int)width height:(int)height {
    [SCShareableContent getShareableContentExcludingDesktopWindows:YES onScreenWindowsOnly:NO
        completionHandler:^(SCShareableContent *content, NSError *error) {
        dispatch_async(self->queue, ^{
            if (self->stopped) return;
            if (error) {
                self->errorMessage = error.localizedDescription;
                return;
            }
            SCWindow *target = nil;
            for (SCWindow *window in content.windows) {
                if (window.windowID == windowID) { target = window; break; }
            }
            if (!target) {
                self->errorMessage = @"Client window is unavailable.";
                return;
            }
            CGSize windowSize = target.frame.size;
            CGFloat aspect = windowSize.width > 0 && windowSize.height > 0
                ? windowSize.width / windowSize.height : (CGFloat)width / height;
            SCStreamConfiguration *config = [SCStreamConfiguration new];
            config.width = MAX(2, MIN(width, (int)round(height * aspect)));
            config.height = MAX(2, MIN(height, (int)round(width / aspect)));
            config.pixelFormat = kCVPixelFormatType_32BGRA;
            config.minimumFrameInterval = CMTimeMake(1, 15);
            config.queueDepth = 3;
            config.showsCursor = NO;
            config.scalesToFit = YES;
            if (@available(macOS 14.0, *)) {
                config.preservesAspectRatio = YES;
            }
            SCContentFilter *filter = [[SCContentFilter alloc] initWithDesktopIndependentWindow:target];
            self->stream = [[SCStream alloc] initWithFilter:filter configuration:config delegate:self];
            NSError *outputError = nil;
            if (![self->stream addStreamOutput:self type:SCStreamOutputTypeScreen
                sampleHandlerQueue:self->queue error:&outputError]) {
                self->errorMessage = outputError.localizedDescription ?: @"Cannot attach capture output.";
                self->stream = nil;
                return;
            }
            self->starting = YES;
            [self->stream startCaptureWithCompletionHandler:^(NSError *startError) {
                dispatch_async(self->queue, ^{
                    self->starting = NO;
                    if (self->stopped) { [self stop]; return; }
                    if (startError) self->errorMessage = startError.localizedDescription;
                });
            }];
        });
    }];
}

- (void)stream:(SCStream *)source didOutputSampleBuffer:(CMSampleBufferRef)sample
    ofType:(SCStreamOutputType)type {
    if (stopped || type != SCStreamOutputTypeScreen || !CMSampleBufferIsValid(sample)) return;
    NSArray *attachments = (__bridge NSArray *)CMSampleBufferGetSampleAttachmentsArray(sample, NO);
    NSNumber *status = attachments.firstObject[SCStreamFrameInfoStatus];
    if (!status || status.integerValue != SCFrameStatusComplete) return;
    CVPixelBufferRef next = CMSampleBufferGetImageBuffer(sample);
    if (!next || CVPixelBufferGetPixelFormatType(next) != kCVPixelFormatType_32BGRA) return;
    CVPixelBufferRetain(next);
    if (frame) CVPixelBufferRelease(frame);
    frame = next;
    sequence++;
    errorMessage = nil;
}

- (void)stream:(SCStream *)source didStopWithError:(NSError *)error {
    dispatch_async(queue, ^{
        if (!self->stopped) self->errorMessage = error.localizedDescription;
    });
}

- (void)stop {
    stopped = YES;
    if (frame) { CVPixelBufferRelease(frame); frame = NULL; }
    if (starting || !stream) return;
    SCStream *closing = stream;
    stream = nil;
    [closing stopCaptureWithCompletionHandler:^(NSError *error) {
        [closing removeStreamOutput:self type:SCStreamOutputTypeScreen error:nil];
    }];
}

- (void)dealloc {
    if (frame) CVPixelBufferRelease(frame);
}
@end

__attribute__((visibility("default")))
int eop_capture_permission(int request) {
    if (CGPreflightScreenCaptureAccess()) return 1;
    return request && CGRequestScreenCaptureAccess() ? 1 : 0;
}

__attribute__((visibility("default")))
void *eop_capture_create(uint32_t windowID, int width, int height) {
    @autoreleasepool {
        if (!windowID || width < 2 || width > 1280 || height < 2 || height > 800) return NULL;
        EOPWindowCapture *capture = [EOPWindowCapture new];
        [capture startWindow:windowID width:width height:height];
        return (__bridge_retained void *)capture;
    }
}

__attribute__((visibility("default")))
uint64_t eop_capture_frame_info(void *handle, int *width, int *height) {
    EOPWindowCapture *capture = (__bridge EOPWindowCapture *)handle;
    __block uint64_t result = 0;
    dispatch_sync(capture->queue, ^{
        if (!capture->frame || capture->stopped || capture->errorMessage) return;
        *width = (int)CVPixelBufferGetWidth(capture->frame);
        *height = (int)CVPixelBufferGetHeight(capture->frame);
        result = capture->sequence;
    });
    return result;
}

__attribute__((visibility("default")))
uint64_t eop_capture_copy_frame(void *handle, void *destination, int stride, int width, int height) {
    EOPWindowCapture *capture = (__bridge EOPWindowCapture *)handle;
    __block uint64_t result = 0;
    dispatch_sync(capture->queue, ^{
        CVPixelBufferRef buffer = capture->frame;
        if (!buffer || capture->stopped || capture->errorMessage || !destination
            || width < 1 || height < 1
            || (size_t)width != CVPixelBufferGetWidth(buffer) || (size_t)height != CVPixelBufferGetHeight(buffer)
            || stride < width * 4) return;
        if (CVPixelBufferLockBaseAddress(buffer, kCVPixelBufferLock_ReadOnly) != kCVReturnSuccess) return;
        const uint8_t *source = CVPixelBufferGetBaseAddress(buffer);
        size_t sourceStride = CVPixelBufferGetBytesPerRow(buffer);
        if (source) {
            for (int y = 0; y < height; y++) {
                memcpy((uint8_t *)destination + (size_t)y * stride, source + (size_t)y * sourceStride, (size_t)width * 4);
            }
            result = capture->sequence;
        }
        CVPixelBufferUnlockBaseAddress(buffer, kCVPixelBufferLock_ReadOnly);
    });
    return result;
}

__attribute__((visibility("default")))
void eop_capture_error(void *handle, char *destination, int capacity) {
    EOPWindowCapture *capture = (__bridge EOPWindowCapture *)handle;
    dispatch_sync(capture->queue, ^{
        if (capacity > 0) snprintf(destination, (size_t)capacity, "%s", capture->errorMessage.UTF8String ?: "");
    });
}

__attribute__((visibility("default")))
void eop_capture_destroy(void *handle) {
    if (!handle) return;
    EOPWindowCapture *capture = (__bridge_transfer EOPWindowCapture *)handle;
    dispatch_async(capture->queue, ^{ [capture stop]; });
}
