using static System.Math;

namespace WarpTen.Simulation
{
    internal sealed class JumpTransition
    {
        public const float LeadSeconds = 2, FlashSeconds = .25f;
        public const float ExitFlashSeconds = .12f, ExitFlashPeak = .2f;
        private bool wasWarp;
        private float flashTime;
        private float exitFlashTime;
        public float Progress { get; private set; }

        public float Flash => Max(Max(0, flashTime / FlashSeconds),
            ExitFlashPeak * Max(0, exitFlashTime / ExitFlashSeconds));

        public void Step(bool inWarp, bool warming, float remaining, float dt)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0)
                return;
            flashTime = Max(0, flashTime - dt);
            exitFlashTime = Max(0, exitFlashTime - dt);
            if (inWarp && !wasWarp && Progress > 0)
                flashTime = FlashSeconds;
            if (!inWarp)
            {
                flashTime = 0;
                if (wasWarp)
                    exitFlashTime = ExitFlashSeconds;
            }
            else
                exitFlashTime = 0;

            Progress = !inWarp && warming && !float.IsNaN(remaining) && !float.IsInfinity(remaining) &&
                       remaining >= 0 && remaining < LeadSeconds
                ? 1 - remaining / LeadSeconds
                : 0;
            wasWarp = inWarp;
        }

        public void Reset()
        {
            Progress = flashTime = exitFlashTime = 0;
            wasWarp = false;
        }
    }
}