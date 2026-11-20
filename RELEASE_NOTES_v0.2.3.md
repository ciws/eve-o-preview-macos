# EVE-O Preview for macOS v0.2.3

Live previews now use Apple's native ScreenCaptureKit instead of launching a screenshot process for every frame.

- Native preview streams at up to 15 FPS, with reusable image buffers and no temporary PNG files.
- Fixed client discovery getting stuck while waiting for System Events. Windows are now discovered through CoreGraphics.
- Added a Screen Recording setup button and clearer permission and capture messages.
- Added a native About panel with the app version and a GitHub link in the app menu.

Download the Apple Silicon DMG below. Requires macOS 12.3 or later. Allow Screen Recording for EVE-O Preview and reopen the app if macOS asks. After updating, macOS may require you to re-enable permission for the new build.

Checked locally on Apple Silicon with a running EVE Online client, native buffer and live-stream tests, app signature verification, and DMG integrity verification. Intel runtime behavior has not been tested. Performance improvements have not yet been measured against the previous release.

The app is ad hoc signed, not Developer ID signed or notarized by Apple.
