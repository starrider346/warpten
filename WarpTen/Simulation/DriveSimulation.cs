using System;
using static System.Math;

namespace WarpTen.Simulation
{
    public sealed class Tuning
    {
        public double ReferenceMass = 10000;
        public double PairStrength = 1;
        public double RecommendedPairs = 5;
        public double MinimumFieldFraction = .25;
        public double MinimumFlowFraction = .1;
        public double ChargeSeconds = 15;
        public double ReserveSeconds = 2;
        public double CoilPlasmaPerSecond => MatrixGrade.CoilRate;

        public double MaximumSpeed = 1e9;
        public double NacelleRate(int coils) => Max(0, coils) * CoilPlasmaPerSecond;

        public void Validate()
        {
            foreach (var v in new[]
                     {
                         ReferenceMass,
                         PairStrength,
                         RecommendedPairs,
                         ChargeSeconds,
                         ReserveSeconds,
                         MaximumSpeed
                     }
                    )
                if (!Finite(v) || v <= 0)
                    throw new ArgumentException("WarpTen tuning must be finite and positive.");
            if (!(MinimumFieldFraction > 0 && MinimumFieldFraction <= 1) ||
                !(MinimumFlowFraction > 0 && MinimumFlowFraction < 1))
                throw new ArgumentException("Invalid WarpTen threshold.");
        }

        public static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
        public static double Clamp(double v) => Finite(v) ? Max(0, Min(1, v)) : 0;
    }

    public enum DrivePhase
    {
        Offline,
        Blocked,
        Charging,
        Ready,
        Warping
    }

    public sealed class DriveSimulation
    {
        private double lastSupply;
        public double Timer { get; private set; }
        public double LeftCharge { get; private set; }
        public double RightCharge { get; private set; }
        public double Strength { get; private set; }
        public double Recommended { get; private set; }
        public double SpeedCeiling { get; private set; }
        public double FullChargeSpeedCeiling { get; private set; }
        public double Speed { get; private set; }
        public DrivePhase Phase { get; private set; }
        public string Status { get; private set; } = "Offline";
        public bool WantsWarp => Phase == DrivePhase.Ready || Phase == DrivePhase.Warping;

        public void Step(Tuning t, double dt, bool enabled, string blocker, double mass, double rawStrength,
            double leftFlow, double rightFlow, bool inWarp, bool canWarp, int leftCoils = 5, int rightCoils = 5)
        {
            if (!Tuning.Finite(dt) || dt <= 0)
                return;
            Recommended = t.RecommendedPairs * t.PairStrength * Max(1, mass) / t.ReferenceMass;
            var left = leftCoils > 0 ? Tuning.Clamp(leftFlow / t.NacelleRate(leftCoils)) : 0;
            var right = rightCoils > 0 ? Tuning.Clamp(rightFlow / t.NacelleRate(rightCoils)) : 0;
            var supplied = WarpSpeedPolicy.Sustained(left, t.MinimumFlowFraction) &&
                           WarpSpeedPolicy.Sustained(right, t.MinimumFlowFraction);
            if (!enabled || blocker != null)
            {
                Timer = LeftCharge = RightCharge = Speed = Strength = SpeedCeiling = FullChargeSpeedCeiling = 0;
                Phase = enabled ? DrivePhase.Blocked : DrivePhase.Offline;
                Status = blocker ?? "Disabled";
                return;
            }

            if (inWarp)
            {
                LeftCharge = Tuning.Clamp(LeftCharge + (WarpSpeedPolicy.Sustained(left, t.MinimumFlowFraction)
                    ? dt / t.ChargeSeconds
                    : -dt / t.ReserveSeconds));
                RightCharge = Tuning.Clamp(RightCharge + (WarpSpeedPolicy.Sustained(right, t.MinimumFlowFraction)
                    ? dt / t.ChargeSeconds
                    : -dt / t.ReserveSeconds));
            }
            else if (supplied && canWarp && rawStrength / Recommended >= t.MinimumFieldFraction)
            {
                Timer = Timer + dt >= t.ChargeSeconds - 1e-9 ? t.ChargeSeconds : Timer + dt;
                LeftCharge = RightCharge = Timer / t.ChargeSeconds;
            }
            else
            {
                Timer = LeftCharge = RightCharge = 0;
            }

            Strength = rawStrength * Min(LeftCharge, RightCharge);
            FullChargeSpeedCeiling = t.MaximumSpeed * Tuning.Clamp(rawStrength / Recommended);
            SpeedCeiling = t.MaximumSpeed * Tuning.Clamp(Strength / Recommended);
            if (supplied)
                lastSupply = Min(left, right);
            var supply = inWarp && !supplied
                ? Min(lastSupply, Min(LeftCharge, RightCharge))
                : Min(left, right);
            Speed = FullChargeSpeedCeiling > 0
                ? WarpSpeedPolicy.FromFlow(FullChargeSpeedCeiling, supply, t.MinimumFlowFraction) *
                  (SpeedCeiling / FullChargeSpeedCeiling)
                : 0;
            if ((inWarp || Timer >= t.ChargeSeconds) && Speed < WarpSpeedPolicy.MinimumSpeed)
            {
                Timer = LeftCharge = RightCharge = Speed = Strength = SpeedCeiling = 0;
                Phase = DrivePhase.Blocked;
                Status = "Below minimum warp speed (1 km/s)";
                return;
            }

            var enough = Strength / Recommended >= t.MinimumFieldFraction;
            if (inWarp && enough)
            {
                Phase = DrivePhase.Warping;
                Status = supplied ? "Warping" : "Warping on reserve";
            }
            else if (!inWarp && Timer >= t.ChargeSeconds && enough)
            {
                Phase = DrivePhase.Ready;
                Status = "Ready for FTL";
            }
            else if (!canWarp && !inWarp)
            {
                Phase = DrivePhase.Blocked;
                Status = "Native warp restrictions";
            }
            else if (rawStrength / Recommended < t.MinimumFieldFraction)
            {
                Phase = DrivePhase.Blocked;
                Status = "Insufficient field strength";
            }
            else if (!supplied || inWarp)
            {
                Phase = DrivePhase.Blocked;
                Status = "Insufficient plasma / reserve depleted";
                Timer = 0;
            }
            else
            {
                Phase = DrivePhase.Charging;
                Status = "Charging";
            }
        }

        public void ConfirmWarpEntry()
        {
            if (Phase != DrivePhase.Ready)
                throw new InvalidOperationException("Only a ready drive can confirm native warp entry.");
            Phase = DrivePhase.Warping;
            Status = "Warping";
        }

        public void Reset()
        {
            Timer = LeftCharge = RightCharge = Speed = Strength = SpeedCeiling = FullChargeSpeedCeiling = 0;
            Phase = DrivePhase.Offline;
        }
    }
}