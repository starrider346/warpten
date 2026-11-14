using static System.Math;

namespace WarpTen.Simulation
{
    public static class WarpSpeedPolicy
    {
        public const double MinimumSpeed = 1000;
        public const string Curve = "capped-logarithmic-v1";
        public const double Sharpness = 1000;

        public static bool Sustained(double flow, double minimumFlow) =>
            Tuning.Finite(flow) && minimumFlow > 0 && flow >= minimumFlow * (1 - 1e-6);

        public static double FromFlow(double ceiling, double flowFraction, double minimumFlow)
        {
            if (!Tuning.Finite(ceiling) || ceiling <= 0 || !Tuning.Finite(minimumFlow) || minimumFlow <= 0 ||
                minimumFlow >= 1)
                return 0;
            var flow = Tuning.Clamp(flowFraction);
            var low = Min(MinimumSpeed, ceiling);
            if (flow <= minimumFlow)
                return Sustained(flow, minimumFlow) ? low : low * flow / minimumFlow;
            if (flow >= 1)
                return ceiling;
            var throttle = (flow - minimumFlow) / (1 - minimumFlow);
            var blend = Log(1 + Sharpness * throttle) / Log(1 + Sharpness);
            return Min(ceiling, low * Exp(Log(ceiling / low) * blend));
        }

        public static double FlowForSpeed(double ceiling, double speed, double minimumFlow)
        {
            if (!Tuning.Finite(ceiling) || ceiling <= 0 || !Tuning.Finite(speed) || speed <= 0 ||
                !Tuning.Finite(minimumFlow) || minimumFlow <= 0 || minimumFlow >= 1)
                return 0;
            var low = Min(MinimumSpeed, ceiling);
            if (speed <= low)
                return minimumFlow;
            if (speed >= ceiling)
                return 1;
            var blend = Log(speed / low) / Log(ceiling / low);
            var throttle = (Exp(blend * Log(1 + Sharpness)) - 1) / Sharpness;
            return minimumFlow + (1 - minimumFlow) * Tuning.Clamp(throttle);
        }
    }
}