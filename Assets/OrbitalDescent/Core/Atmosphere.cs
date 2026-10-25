using System;

namespace OrbitalDescent.Core
{
    public struct AtmosphereSample
    {
        public double Density;      // kg/m^3
        public double Pressure;     // Pa
        public double Temperature;  // K
        public double SpeedOfSound; // m/s
    }

    /// <summary>
    /// Piecewise U.S. Standard Atmosphere 1976 (0 - 86 km) using geopotential altitude, with an
    /// exponential extension above 86 km (isothermal 186.87 K, scale height 6 km, which reproduces
    /// the tabulated ~2e-8 kg/m^3 at 120 km within ~10%). Below 0 m the lowest layer is extrapolated.
    /// </summary>
    public static class Atmosphere
    {
        // Layer base geopotential altitudes (m), base temperatures (K), lapse rates (K/m), base pressures (Pa).
        static readonly double[] BaseAlt = { 0, 11000, 20000, 32000, 47000, 51000, 71000, 84852 };
        static readonly double[] Lapse = { -0.0065, 0.0, 0.001, 0.0028, 0.0, -0.0028, -0.002, 0.0 };
        static readonly double[] BaseTemp = new double[8];
        static readonly double[] BasePress = new double[8];

        const double UpperScaleHeight = 6000.0;
        public const double TopOfAtmosphere = 140000.0;

        static Atmosphere()
        {
            BaseTemp[0] = 288.15;
            BasePress[0] = 101325.0;
            double g0R = Constants.G0 / Constants.AirGasConstant;
            for (int i = 1; i < BaseAlt.Length; i++)
            {
                double dh = BaseAlt[i] - BaseAlt[i - 1];
                double t0 = BaseTemp[i - 1];
                double l = Lapse[i - 1];
                BaseTemp[i] = t0 + l * dh;
                BasePress[i] = l == 0
                    ? BasePress[i - 1] * Math.Exp(-g0R * dh / t0)
                    : BasePress[i - 1] * Math.Pow(BaseTemp[i] / t0, -g0R / l);
            }
        }

        public static double GeopotentialAltitude(double h) => Planet.R * h / (Planet.R + h);

        public static AtmosphereSample Sample(double geometricAltitude)
        {
            double h = Math.Max(-1000.0, geometricAltitude);
            AtmosphereSample s;
            double g0R = Constants.G0 / Constants.AirGasConstant;
            if (h <= 86000.0)
            {
                double hp = GeopotentialAltitude(h);
                int i = BaseAlt.Length - 1;
                while (i > 0 && hp < BaseAlt[i]) i--;
                double dh = hp - BaseAlt[i];
                double l = Lapse[i];
                double t = BaseTemp[i] + l * dh;
                double p = l == 0
                    ? BasePress[i] * Math.Exp(-g0R * dh / BaseTemp[i])
                    : BasePress[i] * Math.Pow(t / BaseTemp[i], -g0R / l);
                s.Temperature = t;
                s.Pressure = p;
            }
            else
            {
                AtmosphereSample top = Sample(86000.0);
                double f = Math.Exp(-(h - 86000.0) / UpperScaleHeight);
                s.Temperature = top.Temperature;
                s.Pressure = top.Pressure * f;
            }
            s.Density = s.Pressure / (Constants.AirGasConstant * s.Temperature);
            s.SpeedOfSound = Math.Sqrt(Constants.AirGamma * Constants.AirGasConstant * s.Temperature);
            if (h > TopOfAtmosphere) { s.Density = 0; s.Pressure = 0; }
            return s;
        }
    }
}
