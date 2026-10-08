# Démo : API Todo .NET 10 + EF Core + Azure SQL sur App Service

Procédure complète, de la création du projet jusqu'à l'API en ligne sur Azure.
Toutes les commandes sont en **PowerShell** et se lancent depuis le dossier de la solution.

**Durée :** environ 20 minutes, dont 5 à 10 d'attente pour la création des ressources Azure.

**Résultat :** une API `https://<app>.azurewebsites.net/api/todos` qui stocke ses données dans Azure SQL.

---

## 0. Prérequis

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.AzureCLI      # rouvrir le terminal après l'installation
az login
az account show --query "{abonnement:name, id:id}"   # vérifier qu'on est sur le bon abonnement
```

---

## Partie 1 : Implémentation

> Le projet `WebApplicationDemo` contient déjà tout ce qui suit. Cette partie sert à le refaire en live pendant la démo.

### 1.1 Créer le projet et ajouter EF Core

```powershell
dotnet new webapi -n WebApplicationDemo --use-controllers
cd WebApplicationDemo
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

### 1.2 L'entité : `Data/Todo.cs`

```csharp
namespace WebApplicationDemo.Data
{
    public class Todo
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public bool IsDone { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
```

### 1.3 La configuration de l'entité : `Data/TodoConfiguration.cs`

Le mapping est défini dans une classe à part plutôt qu'avec des attributs sur l'entité.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace WebApplicationDemo.Data
{
    public class TodoConfiguration : IEntityTypeConfiguration<Todo>
    {
        public void Configure(EntityTypeBuilder<Todo> builder)
        {
            builder.ToTable("Todos");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
            builder.Property(t => t.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
        }
    }
}
```

### 1.4 Le DbContext : `Data/AppDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace WebApplicationDemo.Data
{
    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<Todo> Todos => Set<Todo>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new TodoConfiguration());
        }
    }
}
```

### 1.5 Le contrôleur : `Controllers/TodosController.cs`

Le `DbContext` est injecté directement dans le contrôleur. Il n'y a ni service ni repository intermédiaire.

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplicationDemo.Data;

namespace WebApplicationDemo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TodosController(AppDbContext db) : ControllerBase
    {
        [HttpGet]
        public async Task<IEnumerable<Todo>> GetAll() =>
            await db.Todos.AsNoTracking().ToListAsync();

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Todo>> GetById(int id)
        {
            var todo = await db.Todos.FindAsync(id);
            return todo is null ? NotFound() : todo;
        }

        [HttpPost]
        public async Task<ActionResult<Todo>> Create(Todo todo)
        {
            todo.Id = 0;
            db.Todos.Add(todo);
            await db.SaveChangesAsync();
            return CreatedAtAction(nameof(GetById), new { id = todo.Id }, todo);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, Todo input)
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null) return NotFound();

            todo.Title = input.Title;
            todo.IsDone = input.IsDone;
            await db.SaveChangesAsync();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null) return NotFound();

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();
            return NoContent();
        }
    }
}
```

### 1.6 `Program.cs` : DbContext, CORS et création de la base

```csharp
using Microsoft.EntityFrameworkCore;
using WebApplicationDemo.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
        policy.WithOrigins("http://localhost:4200", "https://mon-front.azurewebsites.net")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure())); // Azure SQL peut être lent à répondre (réveil, failover)

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

// Crée la base et les tables au démarrage si elles n'existent pas (démo, sans migrations)
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
}

app.UseCors("FrontendPolicy");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();
```

### 1.7 Chaîne de connexion locale : `appsettings.json`

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=TodoDemo;Trusted_Connection=True;TrustServerCertificate=True"
}
```

### 1.8 Test en local

```powershell
dotnet run --urls http://localhost:5099
```

Puis, dans un autre terminal :

```powershell
Invoke-RestMethod -Method Post http://localhost:5099/api/todos -ContentType 'application/json' -Body '{"title":"Preparer la demo"}'
Invoke-RestMethod http://localhost:5099/api/todos
```

---

## Partie 2 : Ressources Azure

### 2.1 Variables

Les noms du serveur SQL et de l'application doivent être **uniques dans tout Azure**. Le suffixe aléatoire s'en charge.

```powershell
$suffix   = Get-Random -Maximum 99999
$rg       = "rg-todo-demo"
$location = "francecentral"
$sqlSrv   = "sql-todo-demo-$suffix"
$sqlDb    = "TodoDb"
$sqlUser  = "sqladmin"
$sqlPwd   = "<MotDePasseFort>"          # 8 caractères minimum, avec majuscules, minuscules, chiffres et symboles
$plan     = "plan-todo-demo"
$app      = "app-todo-demo-$suffix"
```

### 2.2 Groupe de ressources

```powershell
az group create -n $rg -l $location
```

### 2.3 Serveur SQL et base de données

```powershell
az sql server create -g $rg -n $sqlSrv -l $location --admin-user $sqlUser --admin-password $sqlPwd

az sql db create -g $rg -s $sqlSrv -n $sqlDb --service-objective Basic
```

> **Option gratuite :** chaque abonnement a droit à une base serverless gratuite. Remplace la deuxième commande par :
> `az sql db create -g $rg -s $sqlSrv -n $sqlDb -e GeneralPurpose -f Gen5 -c 2 --compute-model Serverless --use-free-limit --free-limit-exhaustion-behavior AutoPause`
> Cette base se met en pause quand elle n'est pas utilisée. La première requête après une pause prend quelques dizaines de secondes, d'où le `EnableRetryOnFailure()` dans `Program.cs`.

### 2.4 Pare-feu SQL

```powershell
# Autorise les services Azure, dont l'App Service, à se connecter au serveur
az sql server firewall-rule create -g $rg -s $sqlSrv -n AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

# Autorise ton poste, pour consulter la base depuis SSMS ou Azure Data Studio
$myIp = (Invoke-RestMethod https://api.ipify.org)
az sql server firewall-rule create -g $rg -s $sqlSrv -n MonPoste --start-ip-address $myIp --end-ip-address $myIp
```

### 2.5 Plan App Service et Web App (Windows, .NET 10)

```powershell
az appservice plan create -g $rg -n $plan -l $location --sku B1

# Vérifier le nom exact du runtime .NET 10 dans cette liste
az webapp list-runtimes --os windows

az webapp create -g $rg -p $plan -n $app --runtime "dotnet:10"
```

> Avec `--sku F1`, l'hébergement est gratuit, mais l'application se met en veille et les quotas sont limités. Le plan B1 coûte environ 13 € par mois et convient mieux à une démo.

### 2.6 Chaîne de connexion dans l'App Service

La chaîne de connexion définie dans Azure **remplace** celle de `appsettings.json`. Sans elle, l'application essaie de se connecter à LocalDB, qui n'existe pas sur Azure, et le site renvoie une **erreur 500.30**.

```powershell
$cs = "Server=tcp:$sqlSrv.database.windows.net,1433;Database=$sqlDb;User ID=$sqlUser;Password=$sqlPwd;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

az webapp config connection-string set -g $rg -n $app --connection-string-type SQLAzure --settings DefaultConnection="$cs"
```

---

## Partie 3 : Déploiement

### 3.1 CORS

Dans `Program.cs`, remplace `https://mon-front.azurewebsites.net` par l'URL réelle de ton front. N'ajoute pas de `/` à la fin de l'URL.

Laisse vide la section **CORS** de l'App Service dans le portail. Si elle est remplie, elle prend le pas sur la configuration du code.

### 3.2 Publier et envoyer le zip

```powershell
dotnet publish .\WebApplicationDemo\WebApplicationDemo.csproj -c Release -o .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\app.zip -Force

az webapp deploy -g $rg -n $app --src-path .\app.zip --type zip
```

> **Depuis Visual Studio :** clic droit sur le projet → **Publier** → **Azure** → **Azure App Service (Windows)** → sélectionner `$app` → **Publier**.

### 3.3 Tester en ligne

```powershell
$url = "https://$app.azurewebsites.net/api/todos"

Invoke-RestMethod -Method Post $url -ContentType 'application/json' -Body '{"title":"Hello Azure"}'
Invoke-RestMethod $url
Invoke-RestMethod -Method Put "$url/1" -ContentType 'application/json' -Body '{"title":"Hello Azure","isDone":true}'
Invoke-RestMethod -Method Delete "$url/1"
```

Pour la démo, tu peux aussi montrer les données directement dans la base. Dans le portail, ouvre la base `TodoDb` → **Éditeur de requête** :

```sql
SELECT * FROM Todos;
```

---

## Partie 4 : Dépannage

| Symptôme | Cause probable | Correction |
|---|---|---|
| **500.30** au démarrage | Chaîne de connexion absente ou incorrecte : l'application tente de joindre LocalDB | Étape 2.6, puis `az webapp restart -g $rg -n $app` |
| **500.30** et erreur « Cannot open server… client IP » dans les logs | Pare-feu SQL fermé | Étape 2.4, règle `AllowAzureServices` |
| **500.30** et erreur liée au framework .NET dans les logs | Mauvaise version du runtime | Portail → **Configuration** → **Paramètres généraux** → **.NET 10** |
| Timeout à la première requête | Base serverless en pause | Attendre 30 à 60 secondes puis réessayer : les retries EF absorbent le délai |
| Erreur CORS dans la console du navigateur | Origine absente de la liste, ou CORS rempli dans le portail | Section 3.1 |

Pour voir l'erreur exacte au démarrage :

```powershell
az webapp log config -g $rg -n $app --application-logging filesystem --detailed-error-messages true
az webapp log tail -g $rg -n $app
```

Tu peux aussi passer par **Kudu** (`https://<app>.scm.azurewebsites.net`) → **Debug console** → `cd site\wwwroot` → `dotnet WebApplicationDemo.dll`. L'exception s'affiche directement dans la console.

---

## Partie 5 : Nettoyage après la démo

Cette commande supprime tout : la base, le serveur SQL, l'App Service et le plan.

```powershell
az group delete -n $rg --yes --no-wait
```

---

## Pour aller plus loin (hors démo)

- **Migrations EF :** remplacer `EnsureCreated()` par des migrations versionnées (`dotnet ef migrations add Init`, puis `Database.Migrate()` ou un script SQL dans la CI).
- **Sans mot de passe :** se connecter avec l'identité managée de l'App Service, en ajoutant `Authentication=Active Directory Default` à la chaîne de connexion et en créant l'utilisateur SQL `FROM EXTERNAL PROVIDER`.
- **Secrets :** stocker la chaîne de connexion dans Key Vault et y faire référence depuis l'App Service.
- **CI/CD :** dans le portail, **Centre de déploiement** → GitHub Actions, qui génère le workflow automatiquement.
