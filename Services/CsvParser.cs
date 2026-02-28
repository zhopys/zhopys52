using MiniFinance.Data;
using MiniFinance.Data.Models;
using System.Globalization;

namespace MiniFinance.Services
{
    public interface ICsvParser
    {
        List<Transaction> Parse(Stream fileStream, string userId);
    }

    public class CsvParser : ICsvParser
    {
        private readonly ICategorizationService _categorizationService;

        public CsvParser(ICategorizationService categorizationService)
        {
            _categorizationService = categorizationService;
        }

        public List<Transaction> Parse(Stream fileStream, string userId)
        {
            var transactions = new List<Transaction>();

            using var reader = new StreamReader(fileStream);

            int lineNumber = 0;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                lineNumber++;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                if (lineNumber == 1 && line.Contains("Date") && line.Contains("Amount"))
                    continue;

                var values = line.Split(',');

                if (values.Length >= 3)
                {
                    try
                    {
                        var description = values[2].Trim();
                        var amount = decimal.Parse(values[1].Trim(), CultureInfo.InvariantCulture);
                        var category = values.Length > 3 && !string.IsNullOrWhiteSpace(values[3].Trim())
                            ? values[3].Trim()
                            : _categorizationService.CategorizeTransaction(description, amount);

                        var transaction = new Transaction
                        {
                            Date = DateTime.Parse(values[0].Trim(), CultureInfo.InvariantCulture),
                            Amount = amount,
                            Description = description,
                            Category = category,
                            UserId = userId
                        };

                        transactions.Add(transaction);
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return transactions;
        }
    }
}