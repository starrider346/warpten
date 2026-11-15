using System;

namespace WarpTen.Ponder
{
    internal sealed class GuideTimeline
    {
        internal const float Infusers = 8, Coils = 18, Matrix = 28, Plasma = 40, Spacer = 53,
            Hydrogen = 63, Controls = 75, Clearance = 90, Charge = 104;
        internal readonly float ChargeSeconds, MinimumFlow;
        internal float Complete => Charge + ChargeSeconds;
        internal float Duration => Complete + 10;
        internal GuideTimeline(float chargeSeconds, float minimumFlow)
        {
            if (float.IsNaN(chargeSeconds) || float.IsInfinity(chargeSeconds) || chargeSeconds <= 0 ||
                chargeSeconds > float.MaxValue / 2 || float.IsNaN(minimumFlow) || minimumFlow <= 0 || minimumFlow >= 1)
                throw new ArgumentException("Invalid Ponder charging settings.");
            ChargeSeconds = chargeSeconds;
            MinimumFlow = minimumFlow;
        }

        internal float ChargeAt(float seconds) => Math.Max(0, Math.Min(1, (seconds - Charge) / ChargeSeconds));
    }
}
