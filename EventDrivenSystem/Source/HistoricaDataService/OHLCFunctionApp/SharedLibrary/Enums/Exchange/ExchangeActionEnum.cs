namespace SharedLibrary.Enums.Exchange
{
    // Duplicated from WarmUpService/NotificationService/ExchangeService's own copies — this
    // codebase's established convention is to duplicate small cross-service enums rather than
    // share a project reference. Values/order must stay in sync with those copies.
    public enum ExchangeActionEnum
    {
        Init = 1,
        PreOpen = 2,
        Open = 3,
        PreClose = 4,
        Close = 5,
        TestEntry = 6
    }
}
