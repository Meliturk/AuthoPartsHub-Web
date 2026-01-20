using AutoPartsWeb.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using AutoPartsWeb.Services;
using AutoPartsWeb.Models;
using System.Security.Claims;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddCors(options =>
{
    options.AddPolicy("MobileApp", policy =>
    {
        policy.AllowAnyHeader()
              .AllowAnyMethod()
              .AllowAnyOrigin();
    });
});
builder.Services.AddSession(options =>
{
    // Keep cart for 30 minutes of inactivity
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
})
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.Cookie.Name = "AutoParts.Auth";
        options.SlidingExpiration = true;
    })
    .AddJwtBearer(options =>
    {
        var key = builder.Configuration["Jwt:Key"] ?? "";
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
        };
    });
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

var app = builder.Build();

// Seed or reset default admin
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.EnsureCreated();

    // Schema guard: add ImageUrl column if missing (SQLite)
    using (var conn = db.Database.GetDbConnection())
    {
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(Parts);";
        bool hasImage = false;
        bool hasDescription = false;
        bool hasSeller = false;
        bool hasCondition = false;
        bool hasSellerTable = false;
        bool hasQuestionTable = false;
        bool hasReviewTable = false;
        bool hasPartBrandTable = false;
        bool hasStartYear = false;
        bool hasEndYear = false;
        bool hasVehicleImage = false;
        bool hasBrandLogoUrl = false;
        bool hasResetToken = false;
        bool hasResetExpires = false;
        bool hasEmailConfirmed = false;
        bool hasEmailConfirmToken = false;
        bool hasEmailConfirmExpires = false;
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var colName = reader.GetString(1);
                if (string.Equals(colName, "ImageUrl", StringComparison.OrdinalIgnoreCase))
                {
                    hasImage = true;
                }
                if (string.Equals(colName, "Description", StringComparison.OrdinalIgnoreCase))
                {
                    hasDescription = true;
                }
                if (string.Equals(colName, "SellerId", StringComparison.OrdinalIgnoreCase))
                {
                    hasSeller = true;
                }
                if (string.Equals(colName, "Condition", StringComparison.OrdinalIgnoreCase))
                {
                    hasCondition = true;
                }
                if (string.Equals(colName, "StartYear", StringComparison.OrdinalIgnoreCase))
                {
                    hasStartYear = true;
                }
                if (string.Equals(colName, "EndYear", StringComparison.OrdinalIgnoreCase))
                {
                    hasEndYear = true;
                }
            }
        }

        if (!hasImage)
        {
            using var alter = conn.CreateCommand();
            alter.CommandText = "ALTER TABLE Parts ADD COLUMN ImageUrl TEXT NULL;";
            alter.ExecuteNonQuery();
        }

        if (!hasDescription)
        {
            using var alter2 = conn.CreateCommand();
            alter2.CommandText = "ALTER TABLE Parts ADD COLUMN Description TEXT NULL;";
            alter2.ExecuteNonQuery();
        }

        if (!hasSeller)
        {
            using var alter3 = conn.CreateCommand();
            alter3.CommandText = "ALTER TABLE Parts ADD COLUMN SellerId INTEGER NULL;";
            alter3.ExecuteNonQuery();
        }

        if (!hasCondition)
        {
            using var alter4 = conn.CreateCommand();
            alter4.CommandText = "ALTER TABLE Parts ADD COLUMN Condition TEXT NULL;";
            alter4.ExecuteNonQuery();
        }

        // Vehicles: StartYear/EndYear
        using (var vInfo = conn.CreateCommand())
        {
            vInfo.CommandText = "PRAGMA table_info(Vehicles);";
            using var vr = vInfo.ExecuteReader();
            while (vr.Read())
            {
                var col = vr.GetString(1);
                if (string.Equals(col, "StartYear", StringComparison.OrdinalIgnoreCase)) hasStartYear = true;
                if (string.Equals(col, "EndYear", StringComparison.OrdinalIgnoreCase)) hasEndYear = true;
                if (string.Equals(col, "ImageUrl", StringComparison.OrdinalIgnoreCase)) hasVehicleImage = true;
                if (string.Equals(col, "BrandLogoUrl", StringComparison.OrdinalIgnoreCase)) hasBrandLogoUrl = true;
            }
        }
        if (!hasStartYear)
        {
            using var addStart = conn.CreateCommand();
            addStart.CommandText = "ALTER TABLE Vehicles ADD COLUMN StartYear INTEGER NULL;";
            addStart.ExecuteNonQuery();
        }
        if (!hasEndYear)
        {
            using var addEnd = conn.CreateCommand();
            addEnd.CommandText = "ALTER TABLE Vehicles ADD COLUMN EndYear INTEGER NULL;";
            addEnd.ExecuteNonQuery();
        }
        if (!hasVehicleImage)
        {
            using var addVehicleImage = conn.CreateCommand();
            addVehicleImage.CommandText = "ALTER TABLE Vehicles ADD COLUMN ImageUrl TEXT NULL;";
            addVehicleImage.ExecuteNonQuery();
        }
        if (!hasBrandLogoUrl)
        {
            using var addBrandLogoUrl = conn.CreateCommand();
            addBrandLogoUrl.CommandText = "ALTER TABLE Vehicles ADD COLUMN BrandLogoUrl TEXT NULL;";
            addBrandLogoUrl.ExecuteNonQuery();
        }

        // AppUsers: password reset columns
        using (var uInfo = conn.CreateCommand())
        {
            uInfo.CommandText = "PRAGMA table_info(AppUsers);";
            using var ur = uInfo.ExecuteReader();
            while (ur.Read())
            {
                var col = ur.GetString(1);
                if (string.Equals(col, "PasswordResetTokenHash", StringComparison.OrdinalIgnoreCase))
                    hasResetToken = true;
                if (string.Equals(col, "PasswordResetExpiresAt", StringComparison.OrdinalIgnoreCase))
                    hasResetExpires = true;
            }
        }
        if (!hasResetToken)
        {
            using var addResetToken = conn.CreateCommand();
            addResetToken.CommandText = "ALTER TABLE AppUsers ADD COLUMN PasswordResetTokenHash TEXT NULL;";
            addResetToken.ExecuteNonQuery();
        }
        if (!hasResetExpires)
        {
            using var addResetExpires = conn.CreateCommand();
            addResetExpires.CommandText = "ALTER TABLE AppUsers ADD COLUMN PasswordResetExpiresAt TEXT NULL;";
            addResetExpires.ExecuteNonQuery();
        }

        // AppUsers: email confirmation columns
        using (var eInfo = conn.CreateCommand())
        {
            eInfo.CommandText = "PRAGMA table_info(AppUsers);";
            using var er = eInfo.ExecuteReader();
            while (er.Read())
            {
                var col = er.GetString(1);
                if (string.Equals(col, "EmailConfirmed", StringComparison.OrdinalIgnoreCase))
                    hasEmailConfirmed = true;
                if (string.Equals(col, "EmailConfirmTokenHash", StringComparison.OrdinalIgnoreCase))
                    hasEmailConfirmToken = true;
                if (string.Equals(col, "EmailConfirmExpiresAt", StringComparison.OrdinalIgnoreCase))
                    hasEmailConfirmExpires = true;
            }
        }
        if (!hasEmailConfirmed)
        {
            using var addEmailConfirmed = conn.CreateCommand();
            addEmailConfirmed.CommandText = "ALTER TABLE AppUsers ADD COLUMN EmailConfirmed INTEGER NOT NULL DEFAULT 1;";
            addEmailConfirmed.ExecuteNonQuery();
        }
        if (!hasEmailConfirmToken)
        {
            using var addEmailToken = conn.CreateCommand();
            addEmailToken.CommandText = "ALTER TABLE AppUsers ADD COLUMN EmailConfirmTokenHash TEXT NULL;";
            addEmailToken.ExecuteNonQuery();
        }
        if (!hasEmailConfirmExpires)
        {
            using var addEmailExpires = conn.CreateCommand();
            addEmailExpires.CommandText = "ALTER TABLE AppUsers ADD COLUMN EmailConfirmExpiresAt TEXT NULL;";
            addEmailExpires.ExecuteNonQuery();
        }

        // Ensure SellerApplications table exists
        using var sellerTableCheck = conn.CreateCommand();
        sellerTableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SellerApplications';";
        hasSellerTable = sellerTableCheck.ExecuteScalar() != null;
        if (!hasSellerTable)
        {
            using var createSeller = conn.CreateCommand();
            createSeller.CommandText = @"
                CREATE TABLE SellerApplications (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    UserId INTEGER NOT NULL,
                    CompanyName TEXT NOT NULL,
                    ContactName TEXT NOT NULL,
                    Phone TEXT NOT NULL,
                    Address TEXT NOT NULL,
                    TaxNumber TEXT NULL,
                    Note TEXT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (UserId) REFERENCES AppUsers(Id) ON DELETE CASCADE
                );
            ";
            createSeller.ExecuteNonQuery();
        }

        // Ensure ProductQuestions table exists
        using var qTableCheck = conn.CreateCommand();
        qTableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ProductQuestions';";
        hasQuestionTable = qTableCheck.ExecuteScalar() != null;
        if (!hasQuestionTable)
        {
            using var createQ = conn.CreateCommand();
            createQ.CommandText = @"
                CREATE TABLE ProductQuestions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartId INTEGER NOT NULL,
                    UserId INTEGER NULL,
                    Question TEXT NOT NULL,
                    Answer TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    AnsweredAt TEXT NULL,
                    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (UserId) REFERENCES AppUsers(Id) ON DELETE SET NULL
                );
            ";
            createQ.ExecuteNonQuery();
        }

        // Ensure ProductReviews table exists
        using var rTableCheck = conn.CreateCommand();
        rTableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ProductReviews';";
        hasReviewTable = rTableCheck.ExecuteScalar() != null;
        if (!hasReviewTable)
        {
            using var createR = conn.CreateCommand();
            createR.CommandText = @"
                CREATE TABLE ProductReviews (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartId INTEGER NOT NULL,
                    UserId INTEGER NULL,
                    Rating INTEGER NOT NULL,
                    Comment TEXT NULL,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (UserId) REFERENCES AppUsers(Id) ON DELETE SET NULL
                );
            ";
            createR.ExecuteNonQuery();
        }

        // Ensure PartVehicles table exists (many-to-many)
        using var tableCheck = conn.CreateCommand();
        tableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='PartVehicles';";
        var exists = tableCheck.ExecuteScalar() != null;
        if (!exists)
        {
            using var create = conn.CreateCommand();
            create.CommandText = @"
                CREATE TABLE PartVehicles (
                    PartId INTEGER NOT NULL,
                    VehicleId INTEGER NOT NULL,
                    PRIMARY KEY (PartId, VehicleId),
                    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE,
                    FOREIGN KEY (VehicleId) REFERENCES Vehicles(Id) ON DELETE CASCADE
                );
            ";
            create.ExecuteNonQuery();
        }

        // Ensure Orders table exists
        using var orderCheck = conn.CreateCommand();
        orderCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Orders';";
        var orderTableExists = orderCheck.ExecuteScalar() != null;
        if (!orderTableExists)
        {
            using var createOrders = conn.CreateCommand();
            createOrders.CommandText = @"
                CREATE TABLE Orders (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreatedAt TEXT NOT NULL,
                    CustomerName TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    Address TEXT NOT NULL,
                    City TEXT NULL,
                    Phone TEXT NULL,
                    Status TEXT NOT NULL,
                    Total REAL NOT NULL
                );
            ";
            createOrders.ExecuteNonQuery();
        }

        // Ensure OrderItems table exists
        using var orderItemCheck = conn.CreateCommand();
        orderItemCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='OrderItems';";
        var orderItemExists = orderItemCheck.ExecuteScalar() != null;
        if (!orderItemExists)
        {
            using var createOrderItems = conn.CreateCommand();
            createOrderItems.CommandText = @"
                CREATE TABLE OrderItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    OrderId INTEGER NOT NULL,
                    PartId INTEGER NOT NULL,
                    Quantity INTEGER NOT NULL,
                    UnitPrice REAL NOT NULL,
                    FOREIGN KEY (OrderId) REFERENCES Orders(Id) ON DELETE CASCADE,
                    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE
                );
            ";
            createOrderItems.ExecuteNonQuery();
        }

        // Ensure ContactMessages table exists
        using var contactCheck = conn.CreateCommand();
        contactCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ContactMessages';";
        var contactExists = contactCheck.ExecuteScalar() != null;
        if (!contactExists)
        {
            using var createContacts = conn.CreateCommand();
            createContacts.CommandText = @"
                CREATE TABLE ContactMessages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CreatedAt TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    Email TEXT NOT NULL,
                    Phone TEXT NULL,
                    Message TEXT NOT NULL,
                    Status TEXT NOT NULL DEFAULT 'Yeni'
                );
            ";
            createContacts.ExecuteNonQuery();
        }

        // Ensure PartImages table exists
        using var partImgCheck = conn.CreateCommand();
        partImgCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='PartImages';";
        var partImgExists = partImgCheck.ExecuteScalar() != null;
        if (!partImgExists)
        {
            using var createPartImgs = conn.CreateCommand();
            createPartImgs.CommandText = @"
                CREATE TABLE PartImages (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    PartId INTEGER NOT NULL,
                    Url TEXT NOT NULL,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY (PartId) REFERENCES Parts(Id) ON DELETE CASCADE
                );
            ";
            createPartImgs.ExecuteNonQuery();
        }
        else
        {
            // Add SortOrder column if missing
            using var colCheck = conn.CreateCommand();
            colCheck.CommandText = "PRAGMA table_info('PartImages');";
            using var reader = colCheck.ExecuteReader();
            var hasSort = false;
            while (reader.Read())
            {
                if (reader["name"]?.ToString() == "SortOrder")
                {
                    hasSort = true;
                    break;
                }
            }
            reader.Close();
            if (!hasSort)
            {
                using var addCol = conn.CreateCommand();
                addCol.CommandText = "ALTER TABLE PartImages ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;";
                addCol.ExecuteNonQuery();
            }
        }

        // Ensure PartBrands table exists
        using var brandTableCheck = conn.CreateCommand();
        brandTableCheck.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='PartBrands';";
        hasPartBrandTable = brandTableCheck.ExecuteScalar() != null;
        if (!hasPartBrandTable)
        {
            using var createBrands = conn.CreateCommand();
            createBrands.CommandText = @"
                CREATE TABLE PartBrands (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL
                );
            ";
            createBrands.ExecuteNonQuery();
        }
    }

    var adminEmail = "admin@site.com";
    var admin = db.AppUsers.FirstOrDefault(u => u.Email == adminEmail);
    if (admin == null)
    {
        db.AppUsers.Add(new AppUser
        {
            FullName = "Admin",
            Email = adminEmail,
            PasswordHash = PasswordHasher.Hash("123"),
            Role = "Admin",
            EmailConfirmed = true
        });
    }
    else
    {
        admin.FullName = "Admin";
        admin.Role = "Admin";
        admin.PasswordHash = PasswordHasher.Hash("123");
        admin.EmailConfirmed = true;
    }

    // Seed vehicle marka/model/yıl listesi (varsa tekrar eklenmez)
    var vehicleSeeds = new[]
    {
        new Vehicle { Brand = "Fiat", Model = "Egea", Year = 2020, Engine = "1.3 Multijet" },
        new Vehicle { Brand = "Fiat", Model = "Doblo", Year = 2015, Engine = "1.6 Multijet" },
        new Vehicle { Brand = "Renault", Model = "Clio", Year = 2018, Engine = "1.5 dCi" },
        new Vehicle { Brand = "Volkswagen", Model = "Golf", Year = 2019, Engine = "1.6 TDI" },
        new Vehicle { Brand = "Volkswagen", Model = "Polo", Year = 2014, Engine = "1.4 TDI" },
        new Vehicle { Brand = "Volkswagen", Model = "Touareg", Year = 2004, Engine = "3.2" },
        new Vehicle { Brand = "Audi", Model = "A4", Year = 2016, Engine = "2.0 TDI" },
        new Vehicle { Brand = "BMW", Model = "3 Serisi", Year = 2017, Engine = "320i" },
        new Vehicle { Brand = "Mercedes", Model = "C Serisi", Year = 2018, Engine = "C200d" },
        new Vehicle { Brand = "Opel", Model = "Astra", Year = 2016, Engine = "1.6 CDTi" },
        new Vehicle { Brand = "Toyota", Model = "Corolla", Year = 2020, Engine = "1.6" },
        new Vehicle { Brand = "Hyundai", Model = "i20", Year = 2019, Engine = "1.4" },
        new Vehicle { Brand = "Peugeot", Model = "3008", Year = 2019, Engine = "1.5 BlueHDi" },
        new Vehicle { Brand = "Ford", Model = "Focus", Year = 2018, Engine = "1.5 TDCi" },
        new Vehicle { Brand = "Nissan", Model = "Qashqai", Year = 2017, Engine = "1.6 dCi" }
    };

    var existingVehicles = db.Vehicles
        .Select(v => new { v.Brand, v.Model, v.Year })
        .ToHashSet();

    foreach (var v in vehicleSeeds)
    {
        if (!existingVehicles.Contains(new { v.Brand, v.Model, v.Year }))
        {
            v.StartYear = v.Year;
            v.EndYear = v.Year;
            db.Vehicles.Add(v);
        }
    }

    // Ensure rich category coverage by adding placeholder parts for missing categories
    var requiredCategories = new[]
    {
        "Motor", "Fren", "Filtre", "Elektrik", "Süspansiyon", "Şanzıman",
        "Lastik", "Jant", "Kaporta", "Aydınlatma", "Egzoz", "Soğutma",
        "Yakıt", "Direksiyon", "İç Donanım", "Dış Aksesuar"
    };

    var existingCategories = db.Parts.Select(p => p.Category).Distinct().ToList();
    var missing = requiredCategories.Where(c => !existingCategories.Contains(c)).ToList();
    if (missing.Any())
    {
        foreach (var cat in missing)
        {
            db.Parts.Add(new Part
            {
                Name = $"{cat} Demo Parça",
                Brand = "Genel",
                Category = cat,
                Price = 1000,
                Stock = 10,
                Condition = "Sıfır",
                Description = $"{cat} kategorisi için örnek parça (otomatik eklendi)."
            });
        }
    }

    // Seed a few part brands for the brand picker
    var seedPartBrands = new[] { "Bosch", "Valeo", "NGK", "MANN", "Sachs", "Mahle", "Brembo" };
    var existingPartBrands = db.PartBrands
        .Select(b => b.Name)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    foreach (var name in seedPartBrands)
    {
        if (!existingPartBrands.Contains(name))
        {
            db.PartBrands.Add(new PartBrand { Name = name });
        }
    }

    db.SaveChanges();
    TextNormalization.NormalizeDatabase(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors("MobileApp");
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
