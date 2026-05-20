namespace MiniFinance.Services
{
    public class TransactionValidationException : Exception
    {
        public TransactionValidationException(string message) : base(message) { }
    }
}
