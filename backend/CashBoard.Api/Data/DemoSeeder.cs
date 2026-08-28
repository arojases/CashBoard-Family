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

        var family = await db.Families.FirstOrDefaultAsync();
        if (family is null)
        {
            family = new Family { Name = "Mi familia", Currency = "CLP" };
            db.Families.Add(family); await db.SaveChangesAsync();
        }

        var visitorExists = await db.Users.AnyAsync(x => x.Role == FamilyRole.Visitor);
        if (!visitorExists)
        {
            // Migración única desde la antigua demo: elimina solo registros financieros ficticios.
            db.Transactions.RemoveRange(db.Transactions);
            db.Budgets.RemoveRange(db.Budgets);
            db.SavingsGoals.RemoveRange(db.SavingsGoals);
            db.Debts.RemoveRange(db.Debts);
            await db.SaveChangesAsync();
        }

        var admin = await db.Users.FirstOrDefaultAsync(x => x.Role == FamilyRole.Admin);
        if (admin is null)
            db.Users.Add(new User { FamilyId = family.Id, Name = "Administrador", Email = "admin@cashboard.cl", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin1234!"), Role = FamilyRole.Admin });
        else
        {
            admin.Email = "admin@cashboard.cl"; admin.Name = "Administrador";
            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin1234!");
        }

        if (!visitorExists)
            db.Users.Add(new User { FamilyId = family.Id, Name = "Visita", Email = "visita@cashboard.cl", PasswordHash = BCrypt.Net.BCrypt.HashPassword("Visita1234!"), Role = FamilyRole.Visitor });

        if (!await db.Categories.AnyAsync(x => x.FamilyId == family.Id))
            db.Categories.AddRange(
                Category(family.Id,"Sueldo",TransactionType.Income,"#42B596"), Category(family.Id,"Otros ingresos",TransactionType.Income,"#42B596"),
                Category(family.Id,"Alimentación",TransactionType.Expense,"#EC826D"), Category(family.Id,"Servicios",TransactionType.Expense,"#E4B548"),
                Category(family.Id,"Transporte",TransactionType.Expense,"#7964D1"), Category(family.Id,"Vivienda",TransactionType.Expense,"#627CDB"),
                Category(family.Id,"Salud",TransactionType.Expense,"#E96D92"), Category(family.Id,"Ocio",TransactionType.Expense,"#8D909D"),
                Category(family.Id,"Otros",TransactionType.Expense,"#8D909D"));
        await db.SaveChangesAsync();
    }

    private static Category Category(Guid familyId,string name,TransactionType type,string color) => new() { FamilyId=familyId,Name=name,Type=type,Color=color };
}
