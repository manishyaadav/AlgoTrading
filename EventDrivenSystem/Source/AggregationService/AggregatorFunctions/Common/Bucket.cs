namespace AggregatorFunctions.Common
{
    public class Bucket
    {
        public string InstrumentName {get;set;} = string.Empty;
        public BucketItem currentBucket {get;set;} = new BucketItem();
        public BucketItem prevBucket {get;set;} = new BucketItem();
    }

    public class BucketItem
    {
        public DateTime startTime {get;set;}
        public DateTime endTime {get;set;}
    }
}