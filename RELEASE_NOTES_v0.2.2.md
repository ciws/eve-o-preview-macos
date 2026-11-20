# EVE-O Preview for macOS v0.2.2

The Clients tab now keeps your selected client when the list refreshes automatically.

- Activate selected client continues to target the client you picked.
- Selection is restored by process and window ID if the client title changes.
- When the selected client closes, selection moves to a remaining client.

Includes PR #12 by @geglne, fixing #11.

Download the Apple Silicon DMG attached below. The app is ad hoc signed and is not notarized by Apple.

The Release build, selection-update regression checks, app signature, and DMG integrity were checked locally. Live testing with logged-in EVE clients is still pending.
