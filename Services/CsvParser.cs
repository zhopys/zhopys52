using MiniFinance.Data;
using System.Globalization;

namespace MiniFinance.Services
{
    public interface ICsvParser
    {
        List<Transaction> Parse(Stream fileStream, string userId);
    }

    public class CsvParser : ICsvParser
    {
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
                        var transaction = new Transaction
                        {
                            Date = DateTime.Parse(values[0].Trim(), CultureInfo.InvariantCulture),
                            Amount = decimal.Parse(values[1].Trim(), CultureInfo.InvariantCulture),
                            Description = values[2].Trim(),
                            Category = values.Length > 3 ? values[3].Trim() : "Прочее",
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