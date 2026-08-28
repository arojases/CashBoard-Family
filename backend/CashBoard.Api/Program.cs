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
builder.Services.AddAuthorization(options => options.AddPolicy("AdminOnly", policy => policy.RequireRole(nameof(FamilyRole.Admin))));
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
        .Select(x => new TransactionResponse(x.Id, x.Description, x.CategoryId, x.Category!.Name, x.Date, x.Amount, x.Type == TransactionType.Income ? "income" : "expense", x.PaymentMethod))
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
    return Results.Created($"/api/transactions/{transaction.Id}", new TransactionResponse(transaction.Id, transaction.Description, category.Id, category.Name, transaction.Date, transaction.Amount, transaction.Type == TransactionType.Income ? "income" : "expense", transaction.PaymentMethod));
}).RequireAuthorization("AdminOnly");

secured.MapPut("/transactions/{id:guid}", async (Guid id, CreateTransactionRequest request, ClaimsPrincipal principal, AppDbContext db) =>
{
    var familyId=principal.FamilyId(); var item=await db.Transactions.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==familyId);
    var category=await db.Categories.FirstOrDefaultAsync(x=>x.Id==request.CategoryId&&x.FamilyId==familyId);
    if(item is null||category is null) return Results.NotFound();
    var type=request.Type.Equals("income",StringComparison.OrdinalIgnoreCase)?TransactionType.Income:TransactionType.Expense;
    if(request.Amount<=0||category.Type!=type) return Results.BadRequest(new{message="Datos de movimiento inválidos."});
    item.Description=request.Description.Trim();item.Amount=request.Amount;item.Type=type;item.CategoryId=category.Id;item.PaymentMethod=request.PaymentMethod;item.Date=request.Date?.ToUniversalTime()??item.Date;
    await db.SaveChangesAsync();return Results.NoContent();
}).RequireAuthorization("AdminOnly");

secured.MapDelete("/transactions/{id:guid}", async (Guid id, ClaimsPrincipal principal, AppDbContext db) =>
{
    var transaction = await db.Transactions.FirstOrDefaultAsync(x => x.Id == id && x.FamilyId == principal.FamilyId());
    if (transaction is null) return Results.NotFound();
    db.Transactions.Remove(transaction); await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization("AdminOnly");

secured.MapGet("/categories", async (ClaimsPrincipal principal, AppDbContext db) =>
    await db.Categories.AsNoTracking().Where(x => x.FamilyId == principal.FamilyId()).OrderBy(x => x.Name)
        .Select(x => new { x.Id, x.Name, type = x.Type == TransactionType.Income ? "income" : "expense", x.Color }).ToListAsync());

secured.MapPost("/categories", async(CategoryRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(string.IsNullOrWhiteSpace(r.Name))return Results.BadRequest();var x=new Category{FamilyId=p.FamilyId(),Name=r.Name.Trim(),Color=r.Color,Type=r.Type=="income"?TransactionType.Income:TransactionType.Expense};db.Add(x);await db.SaveChangesAsync();return Results.Ok(x);}).RequireAuthorization("AdminOnly");
secured.MapPut("/categories/{id:guid}", async(Guid id,CategoryRequest r,ClaimsPrincipal p,AppDbContext db)=>{var x=await db.Categories.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();x.Name=r.Name.Trim();x.Color=r.Color;x.Type=r.Type=="income"?TransactionType.Income:TransactionType.Expense;await db.SaveChangesAsync();return Results.NoContent();}).RequireAuthorization("AdminOnly");
secured.MapDelete("/categories/{id:guid}", async(Guid id,ClaimsPrincipal p,AppDbContext db)=>{var x=await db.Categories.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();if(await db.Transactions.AnyAsync(t=>t.CategoryId==id)||await db.Budgets.AnyAsync(b=>b.CategoryId==id))return Results.Conflict(new{message="La categoría está en uso."});db.Remove(x);await db.SaveChangesAsync();return Results.NoContent();}).RequireAuthorization("AdminOnly");

secured.MapGet("/budgets/current", async(ClaimsPrincipal p,AppDbContext db)=>{var fid=p.FamilyId();var now=DateTime.UtcNow;var budgets=await db.Budgets.AsNoTracking().Include(x=>x.Category).Where(x=>x.FamilyId==fid).ToListAsync();var expenses=await db.Transactions.AsNoTracking().Where(x=>x.FamilyId==fid&&x.Type==TransactionType.Expense&&x.Date.Year==now.Year&&x.Date.Month==now.Month).Select(x=>new{x.CategoryId,x.Amount}).ToListAsync();return budgets.Select(x=>new{id=x.Id,categoryId=x.CategoryId,name=x.Category?.Name??"General",limit=x.Limit,used=x.CategoryId is null?expenses.Sum(e=>e.Amount):expenses.Where(e=>e.CategoryId==x.CategoryId).Sum(e=>e.Amount),x.Month,x.Year});});
secured.MapPost("/budgets", async(BudgetRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(r.Limit<=0||r.Month is<1 or>12||r.Year<2000)return Results.BadRequest(new{message="Límite, mes o año inválidos."});var x=new Budget{FamilyId=p.FamilyId(),CategoryId=r.CategoryId,Limit=r.Limit,Month=r.Month,Year=r.Year};db.Add(x);await db.SaveChangesAsync();return Results.Ok(x);}).RequireAuthorization("AdminOnly");
secured.MapPut("/budgets/{id:guid}", async(Guid id,BudgetRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(r.Limit<=0||r.Month is<1 or>12||r.Year<2000)return Results.BadRequest(new{message="Datos inválidos."});var x=await db.Budgets.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();x.CategoryId=r.CategoryId;x.Limit=r.Limit;x.Month=r.Month;x.Year=r.Year;await db.SaveChangesAsync();return Results.NoContent();}).RequireAuthorization("AdminOnly");
secured.MapDelete("/budgets/{id:guid}", async(Guid id,ClaimsPrincipal p,AppDbContext db)=>await DeleteOwned(db,db.Budgets,id,p.FamilyId())).RequireAuthorization("AdminOnly");

secured.MapGet("/savings-goals", async(ClaimsPrincipal p,AppDbContext db)=>await db.SavingsGoals.AsNoTracking().Where(x=>x.FamilyId==p.FamilyId()).OrderBy(x=>x.TargetDate).ToListAsync());
secured.MapPost("/savings-goals", async(GoalRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(string.IsNullOrWhiteSpace(r.Name)||r.TargetAmount<=0||r.CurrentAmount<0)return Results.BadRequest(new{message="Nombre e importes válidos son obligatorios."});var x=new SavingsGoal{FamilyId=p.FamilyId(),Name=r.Name.Trim(),TargetAmount=r.TargetAmount,CurrentAmount=r.CurrentAmount,TargetDate=r.TargetDate,Description=r.Description};db.Add(x);await db.SaveChangesAsync();return Results.Ok(x);}).RequireAuthorization("AdminOnly");
secured.MapPut("/savings-goals/{id:guid}", async(Guid id,GoalRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(string.IsNullOrWhiteSpace(r.Name)||r.TargetAmount<=0||r.CurrentAmount<0)return Results.BadRequest(new{message="Datos inválidos."});var x=await db.SavingsGoals.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();x.Name=r.Name.Trim();x.TargetAmount=r.TargetAmount;x.CurrentAmount=r.CurrentAmount;x.TargetDate=r.TargetDate;x.Description=r.Description;await db.SaveChangesAsync();return Results.NoContent();}).RequireAuthorization("AdminOnly");
secured.MapDelete("/savings-goals/{id:guid}", async(Guid id,ClaimsPrincipal p,AppDbContext db)=>await DeleteOwned(db,db.SavingsGoals,id,p.FamilyId())).RequireAuthorization("AdminOnly");

secured.MapGet("/debts", async(ClaimsPrincipal p,AppDbContext db)=>await db.Debts.AsNoTracking().Where(x=>x.FamilyId==p.FamilyId()).OrderBy(x=>x.DueDate).ToListAsync());
secured.MapPost("/debts", async(DebtRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(string.IsNullOrWhiteSpace(r.Name)||r.TotalAmount<=0||r.PaidAmount<0||r.PaidAmount>r.TotalAmount||r.Installments<1)return Results.BadRequest(new{message="Los montos o cuotas no son válidos."});var x=new Debt{FamilyId=p.FamilyId(),Name=r.Name.Trim(),Entity=r.Entity,TotalAmount=r.TotalAmount,PaidAmount=r.PaidAmount,DueDate=r.DueDate,Installments=r.Installments};db.Add(x);await db.SaveChangesAsync();return Results.Ok(x);}).RequireAuthorization("AdminOnly");
secured.MapPut("/debts/{id:guid}", async(Guid id,DebtRequest r,ClaimsPrincipal p,AppDbContext db)=>{if(string.IsNullOrWhiteSpace(r.Name)||r.TotalAmount<=0||r.PaidAmount<0||r.PaidAmount>r.TotalAmount||r.Installments<1)return Results.BadRequest(new{message="Datos inválidos."});var x=await db.Debts.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();x.Name=r.Name.Trim();x.Entity=r.Entity;x.TotalAmount=r.TotalAmount;x.PaidAmount=r.PaidAmount;x.DueDate=r.DueDate;x.Installments=r.Installments;await db.SaveChangesAsync();return Results.NoContent();}).RequireAuthorization("AdminOnly");
secured.MapDelete("/debts/{id:guid}", async(Guid id,ClaimsPrincipal p,AppDbContext db)=>await DeleteOwned(db,db.Debts,id,p.FamilyId())).RequireAuthorization("AdminOnly");

secured.MapGet("/users", async(ClaimsPrincipal p,AppDbContext db)=>await db.Users.AsNoTracking().Where(x=>x.FamilyId==p.FamilyId()).OrderBy(x=>x.Name).Select(x=>new UserResponse(x.Id,x.Name,x.Email,x.Role.ToString())).ToListAsync()).RequireAuthorization("AdminOnly");
secured.MapPost("/users", async(UserRequest r,ClaimsPrincipal p,AppDbContext db)=>
{
    var email=r.Email.Trim().ToLowerInvariant();
    if(string.IsNullOrWhiteSpace(r.Name)||!email.Contains('@')||string.IsNullOrWhiteSpace(r.Password)||r.Password.Length<8)return Results.BadRequest(new{message="Nombre, correo y contraseña de al menos 8 caracteres son obligatorios."});
    if(await db.Users.AnyAsync(x=>x.Email==email))return Results.Conflict(new{message="Ese correo ya está registrado."});
    var role=r.Role.Equals("Admin",StringComparison.OrdinalIgnoreCase)?FamilyRole.Admin:FamilyRole.Visitor;
    var x=new User{FamilyId=p.FamilyId(),Name=r.Name.Trim(),Email=email,PasswordHash=BCrypt.Net.BCrypt.HashPassword(r.Password),Role=role};db.Add(x);await db.SaveChangesAsync();return Results.Ok(new UserResponse(x.Id,x.Name,x.Email,x.Role.ToString()));
}).RequireAuthorization("AdminOnly");
secured.MapPut("/users/{id:guid}", async(Guid id,UserRequest r,ClaimsPrincipal p,AppDbContext db)=>
{
    var x=await db.Users.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();var email=r.Email.Trim().ToLowerInvariant();
    if(string.IsNullOrWhiteSpace(r.Name)||!email.Contains('@')||(r.Password?.Length>0&&r.Password.Length<8))return Results.BadRequest(new{message="Datos de usuario inválidos."});
    if(await db.Users.AnyAsync(u=>u.Email==email&&u.Id!=id))return Results.Conflict(new{message="Ese correo ya está registrado."});
    var newRole=r.Role.Equals("Admin",StringComparison.OrdinalIgnoreCase)?FamilyRole.Admin:FamilyRole.Visitor;
    if(x.Role==FamilyRole.Admin&&newRole!=FamilyRole.Admin&&await db.Users.CountAsync(u=>u.FamilyId==p.FamilyId()&&u.Role==FamilyRole.Admin)==1)return Results.Conflict(new{message="Debe existir al menos un administrador."});
    x.Name=r.Name.Trim();x.Email=email;x.Role=newRole;if(!string.IsNullOrWhiteSpace(r.Password))x.PasswordHash=BCrypt.Net.BCrypt.HashPassword(r.Password);await db.SaveChangesAsync();return Results.NoContent();
}).RequireAuthorization("AdminOnly");
secured.MapDelete("/users/{id:guid}", async(Guid id,ClaimsPrincipal p,AppDbContext db)=>
{
    if(id==p.UserId())return Results.Conflict(new{message="No puedes eliminar tu propia cuenta."});var x=await db.Users.FirstOrDefaultAsync(x=>x.Id==id&&x.FamilyId==p.FamilyId());if(x is null)return Results.NotFound();
    if(x.Role==FamilyRole.Admin&&await db.Users.CountAsync(u=>u.FamilyId==p.FamilyId()&&u.Role==FamilyRole.Admin)==1)return Results.Conflict(new{message="Debe existir al menos un administrador."});db.Remove(x);await db.SaveChangesAsync();return Results.NoContent();
}).RequireAuthorization("AdminOnly");
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapGet("/", () => Results.Redirect("/swagger"));
app.Run();

static async Task<IResult> DeleteOwned<T>(AppDbContext db,DbSet<T> set,Guid id,Guid familyId) where T:class
{
    var x=await set.FindAsync(id);if(x is null)return Results.NotFound();
    var property=typeof(T).GetProperty("FamilyId");if(property?.GetValue(x) is not Guid owner||owner!=familyId)return Results.NotFound();
    set.Remove(x);await db.SaveChangesAsync();return Results.NoContent();
}

record LoginRequest(string Email, string Password);
record CreateTransactionRequest(string Description, decimal Amount, string Type, Guid CategoryId, string PaymentMethod, DateTime? Date);
record TransactionResponse(Guid Id, string Name, Guid CategoryId, string Category, DateTime Date, decimal Amount, string Type, string PaymentMethod);
record CategoryRequest(string Name,string Type,string Color);
record BudgetRequest(Guid? CategoryId,decimal Limit,int Month,int Year);
record GoalRequest(string Name,decimal TargetAmount,decimal CurrentAmount,DateTime TargetDate,string Description);
record DebtRequest(string Name,string Entity,decimal TotalAmount,decimal PaidAmount,DateTime DueDate,int Installments);
record UserRequest(string Name,string Email,string Role,string? Password);
record UserResponse(Guid Id,string Name,string Email,string Role);

static class ClaimsExtensions
{
    public static Guid FamilyId(this ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("familyId")!);
    public static Guid UserId(this ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
