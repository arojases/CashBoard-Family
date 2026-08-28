using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CashBoard.Api.Data;
using CashBoard.Api.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
var jwt = builder.Configuration.GetSection("Jwt");
var jwtKey = jwt["Key"] ?? throw new InvalidOperationException("Jwt:Key is required");
var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connection = builder.Configuration.GetConnectionString("DefaultConnection");
    if (builder.Configuration["DatabaseProvider"]?.Equals("Postgres", StringComparison.OrdinalIgnoreCase) == true)
        options.UseNpgsql(connection);
    else
        options.UseSqlite(connection);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true,
        ValidateIssuerSigningKey = true, ValidIssuer = jwt["Issuer"], ValidAudience = jwt["Audience"],
        IssuerSigningKey = key, ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:4200").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseSwagger(); app.UseSwaggerUI(); app.UseCors(); app.UseAuthentication(); app.UseAuthorization();

await DemoSeeder.InitializeAsync(app.Services);

app.MapPost("/api/auth/login", async (LoginRequest request, AppDbContext db) =>
{
    var email = request.Email.Trim().ToLowerInvariant();
    var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Email == email);
    if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        return Results.Json(new { message = "Correo o contraseña incorrectos." }, statusCode: 401);

    var claims = new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim("familyId", user.FamilyId.ToString()),
        new Claim(ClaimTypes.Role, user.Role.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email)
    };
    var token = new JwtSecurityToken(jwt["Issuer"], jwt["Audience"], claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new
    {
        token = new JwtSecurityTokenHandler().WriteToken(token),
        user = new { user.Id, user.Name, user.Email, role = user.Role.ToString() }
    });
});

var secured = app.MapGroup("/api").RequireAuthorization();

secured.MapGet("/dashboard/summary", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var familyId = principal.FamilyId(); var now = DateTime.UtcNow;
    var transactions = await db.Transactions.AsNoTracking().Where(x => x.FamilyId == familyId && x.Date.Year == now.Year && x.Date.Month == now.Month)
        .Select(x => new { x.Type, x.Amount }).ToListAsync();
    var income = transactions.Where(x => x.Type == TransactionType.Income).Sum(x => x.Amount);
    var expenses = transactions.Where(x => x.Type == TransactionType.Expense).Sum(x => x.Amount);
    var saved = (await db.SavingsGoals.AsNoTracking().Where(x => x.FamilyId == familyId).Select(x => x.CurrentAmount).ToListAsync()).Sum();
    var pendingDebt = (await db.Debts.AsNoTracking().Where(x => x.FamilyId == familyId).Select(x => new { x.TotalAmount, x.PaidAmount }).ToListAsync()).Sum(x => x.TotalAmount - x.PaidAmount);
    return Results.Ok(new
    {
        income, expenses, balance = income - expenses,
        saved, pendingDebt
    });
});

secured.MapGet("/transactions", async (ClaimsPrincipal principal, AppDbContext db) =>
    await db.Transactions.AsNoTracking().Where(x => x.FamilyId == principal.FamilyId())
        .Include(x => x.Category).OrderByDescending(x => x.Date)
        .Select(x => new TransactionResponse(x.Id, x.Description, x.Category!.Name, x.Date, x.Amount, x.Type == TransactionType.Income ? "income" : "expense", x.PaymentMethod))
        .ToListAsync());

secured.MapPost("/transactions", async (CreateTransactionRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Description))
        return Results.ValidationProblem(new Dictionary<string, string[]> { ["transaction"] = ["Descripción y monto mayor a cero son obligatorios."] });
    var familyId = principal.FamilyId();
    var category = await db.Categories.FirstOrDefaultAsync(x => x.FamilyId == familyId && x.Id == request.CategoryId);
    if (category is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["categoryId"] = ["La categoría no existe."] });
    var requestedType = request.Type.Equals("income", StringComparison.OrdinalIgnoreCase) ? TransactionType.Income : TransactionType.Expense;
    if (category.Type != requestedType) return Results.ValidationProblem(new Dictionary<string, string[]> { ["categoryId"] = ["La categoría no corresponde al tipo de movimiento."] });
    var transaction = new Transaction
    {
        FamilyId = familyId, UserId = principal.UserId(), CategoryId = category.Id,
        Type = requestedType,
        Amount = request.Amount, Description = request.Description.Trim(), PaymentMethod = request.PaymentMethod,
        Date = request.Date?.ToUniversalTime() ?? DateTime.UtcNow
    };
    db.Transactions.Add(transaction); await db.SaveChangesAsync();
    return Results.Created($"/api/transactions/{transaction.Id}", new TransactionResponse(transaction.Id, transaction.Description, category.Name, transaction.Date, transaction.Amount, transaction.Type == TransactionType.Income ? "income" : "expense", transaction.PaymentMethod));
});

secured.MapDelete("/transactions/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == principal.FamilyId());
    if (transaction is null) return Results.NotFound();
    db.Transactions.Remove(transaction); await db.SaveChangesAsync();
    return Results.NoContent();
});

secured.MapGet("/categories", async (ClaimsPrincipal principal, AppDbContext db) =>
    await db.Categories.AsNoTracking().Where(x => x.FamilyId == principal.FamilyId()).OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.Name, type = x.Type == TransactionType.Income ? "income" : "expense", x.Color }).ToListAsync());
secured.MapGet("/budgets/current", (ClaimsPrincipal p, AppDbContext db) => db.Budgets.Include(x => x.Category).Where(x => x.FamilyId == p.FamilyId()).ToListAsync());
secured.MapGet("/savings-goals", (ClaimsPrincipal p, AppDbContext db) => db.SavingsGoals.Where(x => x.FamilyId == p.FamilyId()).ToListAsync());
secured.MapGet("/debts", (ClaimsPrincipal p, AppDbContext db) => db.Debts.Where(x => x.FamilyId == p.FamilyId()).ToListAsync());
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.Run();

record LoginRequest(string Email, string Password);
record CreateTransactionRequest(string Description, decimal Amount, string Type, Guid CategoryId, string PaymentMethod, DateTime? Date);
record TransactionResponse(Guid Id, string Name, string Category, DateTime Date, decimal Amount, string Type, string PaymentMethod);

static class ClaimsExtensions
{
    public static Guid FamilyId(this ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("familyId")!);
    public static Guid UserId(this ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
