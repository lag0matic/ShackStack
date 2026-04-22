namespace ShackStack.DecoderHost.Sstv.Harness;

internal static class ImageComparison
{
    public static ImageComparisonResult Measure(byte[] sourceRgb, byte[] decodedRgb)
    {
        if (sourceRgb.Length != decodedRgb.Length)
        {
            throw new ArgumentException("Images must be the same size.");
        }

        double error = 0.0;
        var rA = new double[sourceRgb.Length / 3];
        var gA = new double[sourceRgb.Length / 3];
        var bA = new double[sourceRgb.Length / 3];
        var rB = new double[decodedRgb.Length / 3];
        var gB = new double[decodedRgb.Length / 3];
        var bB = new double[decodedRgb.Length / 3];

        for (var pixel = 0; pixel < sourceRgb.Length / 3; pixel++)
        {
            var offset = pixel * 3;
            var sr = sourceRgb[offset];
            var sg = sourceRgb[offset + 1];
            var sb = sourceRgb[offset + 2];
            var dr = decodedRgb[offset];
            var dg = decodedRgb[offset + 1];
            var db = decodedRgb[offset + 2];

            error += Math.Abs(sr - dr) + Math.Abs(sg - dg) + Math.Abs(sb - db);
            rA[pixel] = sr;
            gA[pixel] = sg;
            bA[pixel] = sb;
            rB[pixel] = dr;
            gB[pixel] = dg;
            bB[pixel] = db;
        }

        return new ImageComparisonResult(
            error / sourceRgb.Length,
            Correlation(rA, rB),
            Correlation(gA, gB),
            Correlation(bA, bB));
    }

    private static double Correlation(double[] a, double[] b)
    {
        var meanA = a.Average();
        var meanB = b.Average();
        double num = 0.0;
        double denA = 0.0;
        double denB = 0.0;
        for (var i = 0; i < a.Length; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            num += da * db;
            denA += da * da;
            denB += db * db;
        }

        if (denA <= 1e-9 || denB <= 1e-9)
        {
            return 0.0;
        }

        return num / Math.Sqrt(denA * denB);
    }
}

internal sealed record ImageComparisonResult(
    double MeanAbsoluteError,
    double RedCorrelation,
    double GreenCorrelation,
    double BlueCorrelation);
