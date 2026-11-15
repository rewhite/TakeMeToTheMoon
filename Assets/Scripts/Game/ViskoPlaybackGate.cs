namespace TakeMeToTheMoon.Game
{
    /// <summary>
    /// Conservative presentation boundary, not a semantic frame identifier. Visko acknowledges
    /// prompts and chunks separately from WebRTC. For morphs, skip the possibly in-flight old
    /// chunk, wait for the next chunk and a playback buffer, then require decoded video progress.
    /// </summary>
    public sealed class ViskoPlaybackGate
    {
        private bool accepted, pending;
        private int chunks, requiredChunks;
        private float readyAfter, bufferSeconds;
        private ulong frameBaseline;

        public void Begin(bool morph, float playbackBufferSeconds)
        {
            pending = true;
            accepted = false;
            chunks = 0;
            requiredChunks = morph ? 2 : 1;
            bufferSeconds = playbackBufferSeconds;
            readyAfter = float.PositiveInfinity;
        }

        public void Message(string type, float now, ulong decodedFrames)
        {
            if (!pending) return;
            if (type == "prompt_accepted") accepted = true;
            if (type != "chunk_complete" || !accepted || chunks >= requiredChunks) return;
            if (++chunks != requiredChunks) return;
            readyAfter = now + bufferSeconds;
            frameBaseline = decodedFrames;
        }

        public bool TryOpen(float now, ulong decodedFrames, bool onScreen)
        {
            if (!pending || !onScreen || now < readyAfter || decodedFrames <= frameBaseline) return false;
            pending = false;
            return true;
        }

        public void Cancel() => pending = false;
    }
}
