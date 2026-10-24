using System;

namespace nyxWM
{
    /// <summary>
    /// Hides the pointer for the whole desktop while the camera gesture runs.
    /// The cursor usually sits over a window owned by another process, so a
    /// thread-local ShowCursor call would not affect it. Instead every standard
    /// system cursor is swapped for a fully transparent one and the user's
    /// cursor scheme is reloaded as soon as the gesture ends.
    /// </summary>
    internal static class CursorHider
    {
        private static readonly object SyncRoot = new object();
        private static bool _isHidden;

        // OCR_NORMAL ... OCR_HELP plus the Windows 10 pin/person cursors.
        private static readonly uint[] SystemCursorIds =
        {
            32512, // OCR_NORMAL
            32513, // OCR_IBEAM
            32514, // OCR_WAIT
            32515, // OCR_CROSS
            32516, // OCR_UP
            32640, // OCR_SIZE
            32641, // OCR_ICON
            32642, // OCR_SIZENWSE
            32643, // OCR_SIZENESW
            32644, // OCR_SIZEWE
            32645, // OCR_SIZENS
            32646, // OCR_SIZEALL
            32647, // OCR_ICOCUR
            32648, // OCR_NO
            32649, // OCR_HAND
            32650, // OCR_APPSTARTING
            32651, // OCR_HELP
            32671, // OCR_PIN
            32672  // OCR_PERSON
        };

        /// <summary>
        /// Replaces all standard system cursors with a transparent 1x1 cursor.
        /// Safe to call repeatedly; only the first call changes the system state.
        /// </summary>
        public static void Hide()
        {
            lock (SyncRoot)
            {
                if (_isHidden)
                    return;

                foreach (uint cursorId in SystemCursorIds)
                {
                    // AND and XOR planes are both zero, so the cursor draws nothing.
                    IntPtr transparent = WinApi.CreateCursor(
                        IntPtr.Zero, 0, 0, 1, 1, new byte[2], new byte[2]);

                    if (transparent == IntPtr.Zero)
                        continue;

                    // SetSystemCursor takes ownership of the handle only on success.
                    if (!WinApi.SetSystemCursor(transparent, cursorId))
                        WinApi.DestroyCursor(transparent);
                }

                _isHidden = true;
            }
        }

        /// <summary>
        /// Restores the cursor scheme that was active before <see cref="Hide"/>.
        /// Safe to call when the cursor was never hidden.
        /// </summary>
        public static void Show()
        {
            lock (SyncRoot)
            {
                if (!_isHidden)
                    return;

                WinApi.SystemParametersInfo(WinApi.SPI_SETCURSORS, 0, IntPtr.Zero, 0);
                _isHidden = false;
            }
        }
    }
}
