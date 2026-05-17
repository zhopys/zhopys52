using MiniFinance.Data.Models;

namespace MiniFinance.Services
{
    public static class ProjectProfitabilityService
    {
        public record ProjectProfitabilityMetrics(
            int ProjectId,
            string ProjectName,
            string? Department,
            ProjectStatus Status,
            decimal TotalIncome,
            decimal TotalExpense,
            decimal Profit,
            decimal Margin,
            decimal ROI,
            int TransactionCount,
            int MonthCount,
            decimal AvgMonthlyIncome,
            decimal AvgMonthlyExpense,
            decimal AvgMonthlyProfit,
            decimal? Budget,
            decimal BudgetVariance,
            decimal? TargetROI,
            bool IsOnTrack
        );

        public record MonthlyProjectProfitability(
            int ProjectId,
            string ProjectName,
            int Year,
            int Month,
            decimal Income,
            decimal Expense,
            decimal Profit,
            decimal Margin
        );

        public record DepartmentSummary(
            string Department,
            int ProjectCount,
            decimal TotalIncome,
            decimal TotalExpense,
            decimal TotalProfit,
            decimal AvgMargin,
            int ActiveProjects
        );

        public static List<ProjectProfitabilityMetrics> ComputeMetrics(
            List<Project> projects,
            List<Transaction> transactions)
        {
            var metrics = new List<ProjectProfitabilityMetrics>();

            foreach (var project in projects)
            {
                var tx = transactions.Where(t => t.ProjectId == project.Id).ToList();
                var income = tx.Where(t => t.Amount > 0).Sum(t => t.Amount);
                var expense = Math.Abs(tx.Where(t => t.Amount < 0).Sum(t => t.Amount));
                var profit = income - expense;
                var margin = income > 0 ? (profit / income) * 100 : 0;
                var roi = expense > 0 ? (income / expense) : 0;

                var months = tx.Any()
                    ? Math.Max(1, (int)((tx.Max(t => t.Date) - tx.Min(t => t.Date)).TotalDays / 30.0) + 1)
                    : 0;

                var budgetVariance = project.Budget.HasValue && project.Budget > 0
                    ? (expense / project.Budget.Value) * 100
                    : 0;

                var isOnTrack = project.TargetROI.HasValue
                    ? roi >= project.TargetROI.Value
                    : (project.Budget.HasValue ? budgetVariance <= 100 : true);

                metrics.Add(new ProjectProfitabilityMetrics(
                    ProjectId: project.Id,
                    ProjectName: project.Name,
                    Department: project.Department,
                    Status: project.Status,
                    TotalIncome: income,
                    TotalExpense: expense,
                    Profit: profit,
                    Margin: margin,
                    ROI: roi,
                    TransactionCount: tx.Count,
                    MonthCount: months,
                    AvgMonthlyIncome: months > 0 ? income / months : 0,
                    AvgMonthlyExpense: months > 0 ? expense / months : 0,
                    AvgMonthlyProfit: months > 0 ? profit / months : 0,
                    Budget: project.Budget,
                    BudgetVariance: budgetVariance,
                    TargetROI: project.TargetROI,
                    IsOnTrack: isOnTrack
                ));
            }

            return metrics.OrderByDescending(m => m.Profit).ToList();
        }

        public static List<MonthlyProjectProfitability> ComputeMonthlyTrends(
            Project project,
            List<Transaction> transactions)
        {
            var tx = transactions.Where(t => t.ProjectId == project.Id).ToList();

            return tx
                .GroupBy(t => new { t.Date.Year, t.Date.Month })
                .Select(g =>
                {
                    var income = g.Where(t => t.Amount > 0).Sum(t => t.Amount);
                    var expense = Math.Abs(g.Where(t => t.Amount < 0).Sum(t => t.Amount));
                    var profit = income - expense;
                    var margin = income > 0 ? (profit / income) * 100 : 0;

                    return new MonthlyProjectProfitability(
                        ProjectId: project.Id,
                        ProjectName: project.Name,
                        Year: g.Key.Year,
                        Month: g.Key.Month,
                        Income: income,
                        Expense: expense,
                        Profit: profit,
                        Margin: margin
                    );
                })
                .OrderBy(m => m.Year)
                .ThenBy(m => m.Month)
                .ToList();
        }

        public static List<DepartmentSummary> ComputeDepartmentSummaries(
            List<Project> projects,
            List<Transaction> transactions)
        {
            var grouped = projects
                .Where(p => !string.IsNullOrWhiteSpace(p.Department))
                .GroupBy(p => p.Department!)
                .Select(g =>
                {
                    var projectIds = g.Select(p => p.Id).ToHashSet();
                    var tx = transactions.Where(t => projectIds.Contains(t.ProjectId ?? 0)).ToList();
                    var income = tx.Where(t => t.Amount > 0).Sum(t => t.Amount);
                    var expense = Math.Abs(tx.Where(t => t.Amount < 0).Sum(t => t.Amount));
                    var profit = income - expense;
                    var avgMargin = income > 0 ? (profit / income) * 100 : 0;
                    var activeProjects = g.Count(p => p.Status == ProjectStatus.Active);

                    return new DepartmentSummary(
                        Department: g.Key,
                        ProjectCount: g.Count(),
                        TotalIncome: income,
                        TotalExpense: expense,
                        TotalProfit: profit,
                        AvgMargin: avgMargin,
                        ActiveProjects: activeProjects
                    );
                })
                .OrderByDescending(d => d.TotalProfit)
                .ToList();

            return grouped;
        }
    }
}
