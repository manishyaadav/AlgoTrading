using AggregatorFunctions.SharedLibrary.Enums.Candle;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AggregatorFunctions.Common
{
    public class CandleCategories
    {
        public DateTime ForDate { get; set; }
        public int Timeframe { get; set; }
        public CandlePartEnum CandlePart { get; set; }
        public CandleSizeEnum Classification { get; set; }
        public decimal Start { get; set; }
        public decimal End { get; set; }
        public decimal OutlierHigherRange { get; set; }

        public static List<CandleCategories> GetCandleCategories(BoxPlot boxPlot, int timeframe, CandlePartEnum CandlePart, int numOfCategories, DateTime forDate)
        {
            List<CandleCategories> categories = new List<CandleCategories>()
            {
                new CandleCategories()
                {
                    CandlePart = CandlePart,
                    Classification = CandleSizeEnum.Small,
                    Start = 0,
                    End = Math.Round(boxPlot.OutlierHighBoundary / 3, 2),
                    OutlierHigherRange = Math.Round(boxPlot.OutlierHighBoundary,2),
                    ForDate = forDate,
                    Timeframe = timeframe
                },
                new CandleCategories()
                {
                    CandlePart = CandlePart,
                    Classification = CandleSizeEnum.Average,
                    Start = Math.Round(boxPlot.OutlierHighBoundary / 3, 2) + .01m,
                    End = Math.Round((boxPlot.OutlierHighBoundary * 2) / 3, 2),
                    OutlierHigherRange = Math.Round(boxPlot.OutlierHighBoundary,2),
                    ForDate = forDate,
                    Timeframe = timeframe
                },
                new CandleCategories()
                {
                    CandlePart = CandlePart,
                    Classification = CandleSizeEnum.Large,
                    Start = Math.Round((boxPlot.OutlierHighBoundary * 2) / 3, 2) + .01m,
                    End = Math.Round(boxPlot.OutlierHighBoundary,2),
                    OutlierHigherRange = Math.Round(boxPlot.OutlierHighBoundary,2),
                    ForDate = forDate,
                    Timeframe = timeframe
                },
                new CandleCategories()
                {
                    CandlePart = CandlePart,
                    Classification = CandleSizeEnum.Outlier,
                    Start = Math.Round(boxPlot.OutlierHighBoundary,2) + .01m,
                    End = 10000,
                    OutlierHigherRange = Math.Round(boxPlot.OutlierHighBoundary,2),
                    ForDate = forDate,
                    Timeframe = timeframe
                }

            };

            return categories;
        }
    }
}
