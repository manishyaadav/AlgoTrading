using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AggregatorFunctions.Common
{
    public class BoxPlot
    {
        //public List<decimal> Sample { get; set; }
        public decimal Median { get; set; }
        public decimal FirstQuartile { get; set; }
        public decimal ThirdQuartile { get; set; }
        public decimal IQR { get; set; }
        public decimal OutlierHighBoundary { get; set; }
        public decimal OutlierLowBoundary { get; set; }

        public static BoxPlot GetBoxPlot(List<decimal> samples)
        {
            BoxPlot box = new BoxPlot();
            //box.Sample = null;
            box.Median = GetMedian(samples);
            box.FirstQuartile = GetFirstQuartile(samples, box.Median);
            box.ThirdQuartile = GetThirdQuartile(samples, box.Median);
            box.IQR = GetIQR(box.ThirdQuartile, box.FirstQuartile);
            box.OutlierHighBoundary = GetOutlierHigherBoundary(box.ThirdQuartile, box.IQR);
            box.OutlierLowBoundary = GetOutlierLowerBoundary(box.FirstQuartile, box.IQR);

            return box;
        }

        public static decimal GetMedian(List<decimal> samples)
        {
            decimal median = 0;

            samples.Sort();

            int count = samples.Count();

            if (count % 2 == 0)
            {
                int halfway = count / 2;
                decimal val1 = samples[halfway];
                decimal val2 = samples[halfway - 1];

                median = (val1 + val2) / 2;
            }
            else
            {
                int halfway = count / 2;

                median = samples[halfway];
            }

            return median;
        }

        public static decimal CalculateStandardDeviation(List<decimal> values)
        {
            decimal mean = values.Average();
            decimal sumOfSquaresOfDifferences = values.Sum(val => (val - mean) * (val - mean));
            decimal stdDeviation = (decimal)Math.Sqrt((double)(sumOfSquaresOfDifferences / values.Count));
            return stdDeviation;
        }

        public static decimal GetFirstQuartile(List<decimal> samples, decimal median)
        {
            return GetMedian(samples.Where(x => x < median).ToList());
        }

        public static decimal GetThirdQuartile(List<decimal> samples, decimal median)
        {
            return GetMedian(samples.Where(x => x > median).ToList());
        }

        public static decimal GetIQR(decimal thirdQuartile, decimal firstQuartile)
        {
            return thirdQuartile - firstQuartile;
        }

        public static decimal GetOutlierHigherBoundary(decimal thirdQuartile, decimal IQR)
        {
            return thirdQuartile + (IQR * 1.5m);
        }

        public static decimal GetOutlierLowerBoundary(decimal firstQuartile, decimal IQR)
        {
            return firstQuartile - (IQR * 1.5m);
        }
    }
}
