using System;

namespace SurveyX.Core
{
    /// <summary>Reference ellipsoid defined by semi-major axis and inverse flattening.</summary>
    public sealed class Ellipsoid
    {
        public string Name { get; }
        public double A { get; }
        public double InvF { get; }
        public double F => 1.0 / InvF;
        public double E2 => F * (2 - F);
        public double E => Math.Sqrt(E2);

        public Ellipsoid(string name, double a, double invF)
        {
            Name = name; A = a; InvF = invF;
        }

        public static readonly Ellipsoid WGS84 = new Ellipsoid("WGS 84", 6378137.0, 298.257223563);
        public static readonly Ellipsoid Helmert1906 = new Ellipsoid("Helmert 1906", 6378200.0, 298.3);
        public static readonly Ellipsoid Clarke1880RGS = new Ellipsoid("Clarke 1880 (RGS)", 6378249.145, 293.465);
        public static readonly Ellipsoid Intl1924 = new Ellipsoid("International 1924", 6378388.0, 297.0);
    }

    /// <summary>7-parameter Helmert transformation from a local datum to WGS 84.
    /// Rotations in arc-seconds, scale in ppm. Convention as published by EPSG.</summary>
    public sealed class Helmert
    {
        public double Tx, Ty, Tz, Rx, Ry, Rz, Ppm;
        public bool CoordinateFrame; // false = position vector

        public static readonly Helmert None = new Helmert();

        public bool IsIdentity => Tx == 0 && Ty == 0 && Tz == 0 && Rx == 0 && Ry == 0 && Rz == 0 && Ppm == 0;

        public static Helmert ThreeParam(double tx, double ty, double tz) =>
            new Helmert { Tx = tx, Ty = ty, Tz = tz };

        public void ToWgs84(ref double x, ref double y, ref double z) => Apply(ref x, ref y, ref z, +1);
        public void FromWgs84(ref double x, ref double y, ref double z) => Apply(ref x, ref y, ref z, -1);

        private void Apply(ref double x, ref double y, ref double z, int dir)
        {
            if (IsIdentity) return;
            const double asec = Math.PI / (180.0 * 3600.0);
            double sgn = CoordinateFrame ? -1 : 1; // coordinate-frame = position-vector with rotations negated
            double rx = sgn * Rx * asec * dir, ry = sgn * Ry * asec * dir, rz = sgn * Rz * asec * dir;
            double s = Ppm * 1e-6 * dir;
            double tx = Tx * dir, ty = Ty * dir, tz = Tz * dir;
            if (dir < 0)
            {
                // inverse: remove translation first, then rotate/scale back (small-angle exact inverse is fine at mm level)
                x += tx; y += ty; z += tz;
                double xi = (1 + s) * (x - rz * y + ry * z);
                double yi = (1 + s) * (rz * x + y - rx * z);
                double zi = (1 + s) * (-ry * x + rx * y + z);
                x = xi; y = yi; z = zi;
                return;
            }
            double xo = tx + (1 + s) * (x - rz * y + ry * z);
            double yo = ty + (1 + s) * (rz * x + y - rx * z);
            double zo = tz + (1 + s) * (-ry * x + rx * y + z);
            x = xo; y = yo; z = zo;
        }
    }

    public static class Geocentric
    {
        public static void FromGeodetic(Ellipsoid el, double latRad, double lonRad, double h,
            out double x, out double y, out double z)
        {
            double sl = Math.Sin(latRad), cl = Math.Cos(latRad);
            double n = el.A / Math.Sqrt(1 - el.E2 * sl * sl);
            x = (n + h) * cl * Math.Cos(lonRad);
            y = (n + h) * cl * Math.Sin(lonRad);
            z = (n * (1 - el.E2) + h) * sl;
        }

        public static void ToGeodetic(Ellipsoid el, double x, double y, double z,
            out double latRad, out double lonRad, out double h)
        {
            lonRad = Math.Atan2(y, x);
            double p = Math.Sqrt(x * x + y * y);
            double lat = Math.Atan2(z, p * (1 - el.E2));
            double n = el.A;
            for (int i = 0; i < 10; i++)
            {
                double sl = Math.Sin(lat);
                n = el.A / Math.Sqrt(1 - el.E2 * sl * sl);
                double hh = p / Math.Cos(lat) - n;
                double newLat = Math.Atan2(z, p * (1 - el.E2 * n / (n + hh)));
                if (Math.Abs(newLat - lat) < 1e-13) { lat = newLat; break; }
                lat = newLat;
            }
            latRad = lat;
            double s2 = Math.Sin(lat);
            n = el.A / Math.Sqrt(1 - el.E2 * s2 * s2);
            h = p / Math.Cos(lat) - n;
        }
    }

    /// <summary>Transverse Mercator using Krüger series to 6th order in n (sub-millimetre within normal zones).</summary>
    public sealed class TransverseMercator
    {
        public double Lat0Deg { get; }
        public double Lon0Deg { get; }
        public double K0 { get; }
        public double FalseEasting { get; }
        public double FalseNorthing { get; }
        public Ellipsoid Ellipsoid { get; }

        private readonly double _A, _e, _m0;
        private readonly double[] _alpha = new double[7];
        private readonly double[] _beta = new double[7];

        public TransverseMercator(Ellipsoid el, double lat0Deg, double lon0Deg, double k0, double fe, double fn)
        {
            Ellipsoid = el; Lat0Deg = lat0Deg; Lon0Deg = lon0Deg; K0 = k0; FalseEasting = fe; FalseNorthing = fn;
            double f = el.F;
            double n = f / (2 - f);
            double n2 = n * n, n3 = n2 * n, n4 = n3 * n, n5 = n4 * n, n6 = n5 * n;
            _A = el.A / (1 + n) * (1 + n2 / 4 + n4 / 64 + n6 / 256);
            _e = el.E;

            _alpha[1] = n / 2 - 2 * n2 / 3 + 5 * n3 / 16 + 41 * n4 / 180 - 127 * n5 / 288 + 7891 * n6 / 37800;
            _alpha[2] = 13 * n2 / 48 - 3 * n3 / 5 + 557 * n4 / 1440 + 281 * n5 / 630 - 1983433 * n6 / 1935360;
            _alpha[3] = 61 * n3 / 240 - 103 * n4 / 140 + 15061 * n5 / 26880 + 167603 * n6 / 181440;
            _alpha[4] = 49561 * n4 / 161280 - 179 * n5 / 168 + 6601661 * n6 / 7257600;
            _alpha[5] = 34729 * n5 / 80640 - 3418889 * n6 / 1995840;
            _alpha[6] = 212378941 * n6 / 319334400;

            _beta[1] = n / 2 - 2 * n2 / 3 + 37 * n3 / 96 - n4 / 360 - 81 * n5 / 512 + 96199 * n6 / 604800;
            _beta[2] = n2 / 48 + n3 / 15 - 437 * n4 / 1440 + 46 * n5 / 105 - 1118711 * n6 / 3870720;
            _beta[3] = 17 * n3 / 480 - 37 * n4 / 840 - 209 * n5 / 4480 + 5569 * n6 / 90720;
            _beta[4] = 4397 * n4 / 161280 - 11 * n5 / 504 - 830251 * n6 / 7257600;
            _beta[5] = 4583 * n5 / 161280 - 108847 * n6 / 3991680;
            _beta[6] = 20648693 * n6 / 638668800;

            _m0 = MeridianArc(Deg2Rad(lat0Deg));
        }

        private static double Deg2Rad(double d) => d * Math.PI / 180.0;

        private double ConformalTan(double lat)
        {
            double s = Math.Sin(lat);
            return Math.Sinh(Atanh(s) - _e * Atanh(_e * s));
        }

        private double MeridianArc(double lat)
        {
            double t = ConformalTan(lat);
            double xi = Math.Atan(t);
            double sum = xi;
            for (int j = 1; j <= 6; j++) sum += _alpha[j] * Math.Sin(2 * j * xi);
            return _A * sum;
        }

        private static double Atanh(double x) => 0.5 * Math.Log((1 + x) / (1 - x));

        public void Forward(double latRad, double lonRad, out double easting, out double northing)
        {
            double dl = lonRad - Deg2Rad(Lon0Deg);
            // normalise longitude difference
            while (dl > Math.PI) dl -= 2 * Math.PI;
            while (dl < -Math.PI) dl += 2 * Math.PI;
            double t = ConformalTan(latRad);
            double xiP = Math.Atan2(t, Math.Cos(dl));
            double etaP = Atanh(Math.Sin(dl) / Math.Sqrt(1 + t * t));
            double xi = xiP, eta = etaP;
            for (int j = 1; j <= 6; j++)
            {
                xi += _alpha[j] * Math.Sin(2 * j * xiP) * Math.Cosh(2 * j * etaP);
                eta += _alpha[j] * Math.Cos(2 * j * xiP) * Math.Sinh(2 * j * etaP);
            }
            easting = FalseEasting + K0 * _A * eta;
            northing = FalseNorthing + K0 * (_A * xi - _m0);
        }

        public void Inverse(double easting, double northing, out double latRad, out double lonRad)
        {
            double xi = (northing - FalseNorthing + K0 * _m0) / (K0 * _A);
            double eta = (easting - FalseEasting) / (K0 * _A);
            double xiP = xi, etaP = eta;
            for (int j = 1; j <= 6; j++)
            {
                xiP -= _beta[j] * Math.Sin(2 * j * xi) * Math.Cosh(2 * j * eta);
                etaP -= _beta[j] * Math.Cos(2 * j * xi) * Math.Sinh(2 * j * eta);
            }
            double chi = Math.Asin(Math.Sin(xiP) / Math.Cosh(etaP));
            lonRad = Deg2Rad(Lon0Deg) + Math.Atan2(Math.Sinh(etaP), Math.Cos(xiP));
            double lat = chi;
            for (int i = 0; i < 20; i++)
            {
                double es = _e * Math.Sin(lat);
                double next = 2 * Math.Atan(Math.Pow((1 + es) / (1 - es), _e / 2) * Math.Tan(chi / 2 + Math.PI / 4)) - Math.PI / 2;
                if (Math.Abs(next - lat) < 1e-14) { lat = next; break; }
                lat = next;
            }
            latRad = lat;
        }

        /// <summary>Point scale factor and grid convergence (degrees) at a geographic position.</summary>
        public void ScaleAndConvergence(double latRad, double lonRad, out double k, out double gammaDeg)
        {
            // numerical derivative — robust and accurate enough for display
            const double d = 1e-7;
            Forward(latRad, lonRad, out double e0, out double n0);
            Forward(latRad, lonRad + d, out double e1, out double n1);
            Forward(latRad + d, lonRad, out double e2, out double n2);
            double sl = Math.Sin(latRad);
            double nu = Ellipsoid.A / Math.Sqrt(1 - Ellipsoid.E2 * sl * sl);
            double rho = nu * (1 - Ellipsoid.E2) / (1 - Ellipsoid.E2 * sl * sl);
            double dEast = Math.Sqrt((e1 - e0) * (e1 - e0) + (n1 - n0) * (n1 - n0));
            double groundEast = nu * Math.Cos(latRad) * d;
            k = dEast / groundEast;
            gammaDeg = Math.Atan2(e2 - e0, n2 - n0) * -180.0 / Math.PI;
            _ = rho;
        }
    }
}
