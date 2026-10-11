namespace GHelper.Overlay
{
    /// <summary>
    /// The on-screen hardware overlay window (legacy HardwareOverlay form). Drawing is done by the UI layer,
    /// which assigns an implementation to <c>Program.hardwareOverlay</c> before <c>Program.Start</c>.
    /// Sampling/formatting/layout lives in <see cref="OverlayModel"/>.
    /// </summary>
    public interface IHardwareOverlay
    {
        void StartOverlay();
        void SetDragKey(bool down);
        void SuspendForDisplayOff();
        void ResumeForDisplayOn();

        /// <summary>Legacy HardwareOverlay.StopOverlay (toggle off, game-only restart). Additive default: no-op.</summary>
        void StopOverlay() { }

        /// <summary>
        /// Legacy HardwareOverlay.RefreshSettings: re-read the overlay_* options after the config window changed them.
        /// Additive default: no-op.
        /// </summary>
        void RefreshSettings() { }
    }
}
