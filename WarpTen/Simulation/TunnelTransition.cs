using static System.Math;

namespace WarpTen.Simulation
{
    internal sealed class TunnelTransition
    {
        public const float PhasePeriod = 4096;
        private double phase;
        public float Intensity { get; private set; }
        public float Phase => (float)phase;

        public void Step(bool eligible, float dt, float speed)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt <= 0)
                return;
            Intensity = eligible ? Min(1, Intensity + dt) : 0;
            if (float.IsNaN(speed) || float.IsInfinity(speed))
                speed = 0;
            phase = (phase + (double)dt * (0.2 + Max(0, Min(1, speed)) * 1.8)) % PhasePeriod;
        }

        public void Reset()
        {
            Intensity = 0;
            phase = 0;
        }
    }
}