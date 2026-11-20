#import "../EveOPreview.Mac/Native/WindowCapture.m"
#import <AppKit/AppKit.h>
#include <assert.h>

int main(int argc, const char *argv[]) {
    @autoreleasepool {
        EOPWindowCapture *capture = [EOPWindowCapture new];
        void *handle = (__bridge_retained void *)capture;
        CVPixelBufferRef pixels = NULL;
        assert(CVPixelBufferCreate(kCFAllocatorDefault, 4, 3, kCVPixelFormatType_32BGRA,
            NULL, &pixels) == kCVReturnSuccess);
        CVPixelBufferLockBaseAddress(pixels, 0);
        for (int y = 0; y < 3; y++) {
            uint8_t *row = CVPixelBufferGetBaseAddress(pixels) + y * CVPixelBufferGetBytesPerRow(pixels);
            for (int x = 0; x < 16; x++) row[x] = (uint8_t)(y * 16 + x);
        }
        CVPixelBufferUnlockBaseAddress(pixels, 0);
        dispatch_sync(capture->queue, ^{ capture->frame = pixels; capture->sequence = 1; });

        int width = 0, height = 0;
        assert(eop_capture_frame_info(handle, &width, &height) == 1 && width == 4 && height == 3);
        uint8_t destination[72];
        memset(destination, 0xAA, sizeof(destination));
        assert(eop_capture_copy_frame(handle, destination, 24, 4, 3) == 1);
        for (int y = 0; y < 3; y++) {
            for (int x = 0; x < 16; x++) assert(destination[y * 24 + x] == y * 16 + x);
            for (int x = 16; x < 24; x++) assert(destination[y * 24 + x] == 0xAA);
        }
        assert(eop_capture_copy_frame(handle, destination, 8, 4, 3) == 0);
        assert(eop_capture_copy_frame(handle, destination, 24, 5, 3) == 0);
        assert(eop_capture_copy_frame(handle, NULL, 24, 4, 3) == 0);
        dispatch_sync(capture->queue, ^{ capture->errorMessage = @"test capture error"; });
        char error[8];
        eop_capture_error(handle, error, sizeof(error));
        assert(strcmp(error, "test ca") == 0);
        assert(eop_capture_frame_info(handle, &width, &height) == 0);
        dispatch_sync(capture->queue, ^{ [capture stop]; });
        assert(eop_capture_copy_frame(handle, destination, 24, 4, 3) == 0);
        eop_capture_destroy(handle);
        puts("PASS: BGRA copy, padded stride, invalid dimensions, errors, and stopped capture.");

        if (argc < 2 || strcmp(argv[1], "--live") != 0) return 0;
        if (!eop_capture_permission(0)) {
            puts("SKIP: live capture requires existing Screen Recording permission.");
            return 0;
        }
        [NSApplication sharedApplication];
        NSWindow *window = [[NSWindow alloc] initWithContentRect:NSMakeRect(50, 50, 320, 200)
            styleMask:NSWindowStyleMaskTitled backing:NSBackingStoreBuffered defer:NO];
        window.title = @"EVE Preview native capture test";
        window.backgroundColor = NSColor.redColor;
        [window orderFrontRegardless];
        void *live = eop_capture_create((uint32_t)window.windowNumber, 160, 100);
        assert(live);
        NSDate *deadline = [NSDate dateWithTimeIntervalSinceNow:10];
        uint64_t latest = 0;
        uint64_t firstFrame = 0;
        while ([deadline timeIntervalSinceNow] > 0) {
            [NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.05]];
            if (eop_capture_frame_info(live, &width, &height) > 0) {
                uint8_t *data = calloc((size_t)width * height, 4);
                latest = eop_capture_copy_frame(live, data, width * 4, width, height);
                free(data);
                if (latest && !firstFrame) {
                    firstFrame = latest;
                    window.backgroundColor = NSColor.blueColor;
                    [window display];
                } else if (latest > firstFrame) {
                    break;
                }
            }
        }
        char liveError[512];
        eop_capture_error(live, liveError, sizeof(liveError));
        eop_capture_destroy(live);
        [window orderOut:nil];
        if (!firstFrame || latest <= firstFrame) {
            fprintf(stderr, "FAIL: live stream did not update: %s\n", liveError);
            return 1;
        }
        printf("PASS: ScreenCaptureKit delivered changing %dx%d frames from the test window.\n", width, height);
    }
    return 0;
}
