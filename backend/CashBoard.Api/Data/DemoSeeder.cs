using CashBoard.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace CashBoard.Api.Data;

public static class DemoSeeder
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
        if (await db.Families.AnyAsync()) return;

        var family = new Family { Name = "Familia Rojas", Currency = "CLP" };
        var user = new User { Family = family, Name = "Ariel Rojas", Email = "demo@cashboard.cl", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Demo1234!"), Role = FamilyRole.Admin };
        var categories = new[]
        {
            new Category { FamilyId = family.Id, Name = "Sueldo", Type = TransactionType.Income, Color = "#42B596" },
            new Category { FamilyId = family.Id, Name = "Otros ingresos", Type = TransactionType.Income, Color = "#42B596" },
            new Category { FamilyId = family.Id, Name = "Alimentación", Type = TransactionType.Expense, Color = "#EC826D" },
            new Category { FamilyId = family.Id, Name = "Servicios", Type = TransactionType.Expense, Color = "#E4B548" },
            new Category { FamilyId = family.Id, Name = "Transporte", Type = TransactionType.Expense, Color = "#7964D1" },
            new Category { FamilyId = family.Id, Name = "Ahorro", Type = TransactionType.Expense, Color = "#627CDB" },
            new Category { FamilyId = family.Id, Name = "Otros", Type = TransactionType.Expense, Color = "#8D909D" }
        };
        db.AddRange(family, user); db.Categories.AddRange(categories); await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.Transactions.AddRange(
            new Transaction { FamilyId = family.Id, UserId = user.Id, CategoryId = categories[0].Id, Type = TransactionType.Income, Amount = 1850000, Description = "Sueldo mensual", Date = now.AddHours(-3), PaymentMethod = "Transferencia" },
            new Transaction { FamilyId = family.Id, UserId = user.Id, CategoryId = categories[2].Id, Type = TransactionType.Expense, Amount = 58490, Description = "Supermercado Jumbo", Date = now.AddHours(-1), PaymentMethod = "Débito" },
            new Transaction { FamilyId = family.Id, UserId = user.Id, CategoryId = categories[3].Id, Type = TransactionType.Expense, Amount = 47600, Description = "Cuenta de electricidad", Date = now.AddDays(-1), PaymentMethod = "Transferencia" },
            new Transaction { FamilyId = family.Id, UserId = user.Id, CategoryId = categories[4].Id, Type = TransactionType.Expense, Amount = 8950, Description = "Uber", Date = now.AddDays(-2), PaymentMethod = "Crédito" });
        db.SavingsGoals.Add(new SavingsGoal { FamilyId = family.Id, Name = "Fondo de emergencia", TargetAmount = 4000000, CurrentAmount = 2400000, TargetDate = now.AddMonths(8) });
        db.Debts.Add(new Debt { FamilyId = family.Id, Name = "Tarjeta de crédito", Entity = "Banco", TotalAmount = 850000, PaidAmount = 430000, DueDate = now.AddDays(12), Installments = 3 });
        await db.SaveChangesAsync();
    }
}
