using System.Collections.Generic;
using System;
using static System.Math;

namespace WarpTen.Simulation
{
    public readonly struct MatrixGrade
    {
        public const double CoilRate = .4;
        public readonly char Code;
        public readonly int RatedCoils;
        public readonly double HydrogenPerPlasma;

        internal MatrixGrade(char code, int coils, double hydrogenPerPlasma)
        {
            Code = code;
            RatedCoils = coils;
            HydrogenPerPlasma = hydrogenPerPlasma;
        }

        public double PlasmaPerHydrogen => 1 / HydrogenPerPlasma;
        public double MaxPlasmaRate => RatedCoils * CoilRate;
        public double MaxHydrogenRate => MaxPlasmaRate * HydrogenPerPlasma;

        public static IReadOnlyList<MatrixGrade> All { get; } = Array.AsReadOnly(new[]
        {
            new MatrixGrade('D', 4, 5), new MatrixGrade('C', 12, 3), new MatrixGrade('B', 24, 2),
            new MatrixGrade('A', 40, 1)
        });
    }

    public readonly struct MatrixProduction
    {
        public readonly double Hydrogen, Plasma;

        public MatrixProduction(double hydrogen, double plasma)
        {
            Hydrogen = hydrogen;
            Plasma = plasma;
        }
    }

    public static class MatrixPolicy
    {
        public static MatrixProduction Produce(MatrixGrade grade, double dt, double hydrogen, double demand)
        {
            if (grade.RatedCoils <= 0 || !Tuning.Finite(dt) || dt <= 0 || !Tuning.Finite(hydrogen) || hydrogen <= 0 ||
                !Tuning.Finite(demand) || demand <= 0)
                return default;
            var plasma = Min(grade.MaxPlasmaRate * dt,
                Min(demand * dt, hydrogen * grade.PlasmaPerHydrogen));
            return new MatrixProduction(plasma * grade.HydrogenPerPlasma, plasma);
        }
    }
}