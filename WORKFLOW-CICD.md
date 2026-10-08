# Comprendre un workflow GitHub Actions (CI/CD .NET vers Azure App Service)

Ce document explique, ligne par ligne, le workflow que génère le **Centre de déploiement** d'Azure quand on relie un App Service à un dépôt GitHub. Il s'appuie sur ce projet : une API .NET 10 déployée sur un App Service Windows.

> Le contenu exact du fichier peut légèrement varier selon la date de génération : numéros de version des actions, nom des secrets… La structure, elle, reste la même.

---

## 1. Où se trouve le workflow et à quoi il sert

- C'est un fichier **YAML** placé dans `.github/workflows/`, par exemple `.github/workflows/master_api-demo.yml`.
- Azure le **commite lui-même** dans ton dépôt quand tu configures le Centre de déploiement. Pense à faire un `git pull` ensuite.
- GitHub le lit et l'exécute automatiquement à chaque événement prévu (un push, par exemple).

**CI (intégration continue)** : à chaque push, le code est récupéré, compilé et éventuellement testé. On sait tout de suite si le code est cassé.

**CD (déploiement continu)** : si la CI réussit, la nouvelle version est automatiquement mise en ligne sur Azure.

---

## 2. Le vocabulaire

| Terme | Définition | Dans notre workflow |
|---|---|---|
| **Workflow** | Le fichier entier : un processus automatisé | `Build and deploy ASP.Net Core app…` |
| **Event** (déclencheur) | Ce qui lance le workflow | Un `push` sur `master`, ou un lancement manuel |
| **Job** | Un groupe d'étapes exécutées sur une même machine | `build` et `deploy` |
| **Runner** | La machine virtuelle, fournie par GitHub, qui exécute un job | `windows-latest` |
| **Step** (étape) | Une action ou une commande, exécutées dans l'ordre | `dotnet build`, `dotnet publish`… |
| **Action** | Une étape toute faite et réutilisable, appelée avec `uses:` | `actions/checkout`, `azure/webapps-deploy`… |
| **Artifact** | Un fichier ou dossier produit par un job et transmis à un autre | Le dossier publié de l'API |
| **Secret** | Une valeur sensible stockée chiffrée dans GitHub, jamais affichée | Les identifiants Azure |
| **Environment** | Une cible de déploiement nommée, visible dans GitHub | `Production` |

---

## 3. Le workflow complet

```yaml
name: Build and deploy ASP.Net Core app to Azure Web App - api-demo

on:
  push:
    branches:
      - master
  workflow_dispatch:

jobs:
  build:
    runs-on: windows-latest
    permissions:
      contents: read

    steps:
      - uses: actions/checkout@v4

      - name: Set up .NET Core
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'

      - name: Build with dotnet
        run: dotnet build --configuration Release

      - name: dotnet publish
        run: dotnet publish -c Release -o "${{env.DOTNET_ROOT}}/myapp"

      - name: Upload artifact for deployment job
        uses: actions/upload-artifact@v4
        with:
          name: .net-app
          path: ${{env.DOTNET_ROOT}}/myapp

  deploy:
    runs-on: windows-latest
    needs: build
    environment:
      name: 'Production'
      url: ${{ steps.deploy-to-webapp.outputs.webapp-url }}
    permissions:
      id-token: write
      contents: read

    steps:
      - name: Download artifact from build job
        uses: actions/download-artifact@v4
        with:
          name: .net-app

      - name: Login to Azure
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZUREAPPSERVICE_CLIENTID_XXXX }}
          tenant-id: ${{ secrets.AZUREAPPSERVICE_TENANTID_XXXX }}
          subscription-id: ${{ secrets.AZUREAPPSERVICE_SUBSCRIPTIONID_XXXX }}

      - name: Deploy to Azure Web App
        id: deploy-to-webapp
        uses: azure/webapps-deploy@v3
        with:
          app-name: 'api-demo'
          slot-name: 'Production'
          package: .
```

---

## 4. Explication bloc par bloc

### 4.1 Le nom

```yaml
name: Build and deploy ASP.Net Core app to Azure Web App - api-demo
```
C'est le nom affiché dans l'onglet **Actions** de GitHub. Il est purement informatif.

### 4.2 Les déclencheurs : `on`

```yaml
on:
  push:
    branches:
      - master
  workflow_dispatch:
```
- `push` sur `master` : le workflow se lance à chaque push sur la branche `master`. Un push sur une autre branche ne déclenche rien.
- `workflow_dispatch` : ajoute un bouton **Run workflow** dans l'onglet Actions, pour lancer le workflow à la main sans pousser de code. C'est pratique en démo.

Autres déclencheurs utiles :

```yaml
on:
  pull_request:            # vérifie le build sur chaque pull request, sans déployer
    branches: [master]
  push:
    branches: [master]
    paths:                 # ne se lance que si ces fichiers changent
      - 'WebApplicationDemo/**'
  schedule:
    - cron: '0 6 * * 1'    # tous les lundis à 6h UTC
```

### 4.3 Les jobs

```yaml
jobs:
  build:   ...
  deploy:  ...
```
Le workflow contient **deux jobs**. Chacun tourne sur **sa propre machine virtuelle, vierge**. C'est pourquoi le résultat du build doit être transmis au déploiement sous forme d'**artifact** (voir 4.6).

### 4.4 Le job `build` : sa machine et ses droits

```yaml
  build:
    runs-on: windows-latest
    permissions:
      contents: read
```
- `runs-on` : le type de machine. `windows-latest` correspond à notre App Service Windows. `ubuntu-latest` marcherait aussi pour du .NET, et c'est plus rapide.
- `permissions: contents: read` : le job a seulement le droit de **lire** le dépôt. C'est le principe du moindre privilège.

### 4.5 Les étapes du build

```yaml
      - uses: actions/checkout@v4
```
**Récupère le code** du dépôt sur la machine. Sans cette étape, la machine est vide. `@v4` fixe la version de l'action.

```yaml
      - name: Set up .NET Core
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
```
**Installe le SDK .NET 10.** `with:` passe des paramètres à l'action. `10.x` veut dire « la dernière version 10 disponible ». Cette version doit correspondre au `TargetFramework` du `.csproj` (`net10.0`) et à la pile d'exécution de l'App Service.

```yaml
      - name: Build with dotnet
        run: dotnet build --configuration Release
```
**Compile le projet.** `run:` exécute une commande shell, comme dans un terminal. La commande est lancée à la racine du dépôt : elle trouve `WebApplicationDemo.slnx` et compile la solution. **Si la compilation échoue, le workflow s'arrête là et rien n'est déployé.**

```yaml
      - name: dotnet publish
        run: dotnet publish -c Release -o "${{env.DOTNET_ROOT}}/myapp"
```
**Prépare les fichiers à déployer** : les DLL, `appsettings.json`, `web.config`… dans le dossier `myapp`. C'est l'équivalent du bouton **Publier** de Visual Studio, sans l'envoi vers Azure.
- `${{ ... }}` est une **expression** GitHub, remplacée par sa valeur au moment de l'exécution.
- `env.DOTNET_ROOT` est une variable d'environnement créée par `setup-dotnet`.

Les **migrations EF** du dossier `Migrations/` sont compilées dans la DLL à cette étape. C'est grâce à ça que `Database.Migrate()` peut les appliquer au démarrage sur Azure.

```yaml
      - name: Upload artifact for deployment job
        uses: actions/upload-artifact@v4
        with:
          name: .net-app
          path: ${{env.DOTNET_ROOT}}/myapp
```
**Sauvegarde le dossier publié** sous le nom `.net-app`. Il devient téléchargeable dans GitHub : onglet Actions, puis le run concerné, section **Artifacts**.

### 4.6 Le job `deploy` : sa machine, sa dépendance et son environnement

```yaml
  deploy:
    runs-on: windows-latest
    needs: build
    environment:
      name: 'Production'
      url: ${{ steps.deploy-to-webapp.outputs.webapp-url }}
    permissions:
      id-token: write
      contents: read
```
- `needs: build` : ce job **attend la réussite de `build`**. Si le build échoue, le déploiement n'a pas lieu. Sans `needs`, les deux jobs tourneraient en parallèle.
- `environment` : déclare un environnement GitHub nommé `Production`. L'URL du site s'affiche alors directement dans le run. On peut aussi y exiger une **approbation manuelle** avant le déploiement : GitHub → **Settings** → **Environments** → **Production** → **Required reviewers**.
- `permissions: id-token: write` : autorise le job à demander un **jeton OIDC** à GitHub, utilisé pour se connecter à Azure sans mot de passe (voir 4.7).

### 4.7 Les étapes du déploiement

```yaml
      - name: Download artifact from build job
        uses: actions/download-artifact@v4
        with:
          name: .net-app
```
**Récupère le dossier publié** par le job `build` sur cette nouvelle machine.

```yaml
      - name: Login to Azure
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZUREAPPSERVICE_CLIENTID_XXXX }}
          tenant-id: ${{ secrets.AZUREAPPSERVICE_TENANTID_XXXX }}
          subscription-id: ${{ secrets.AZUREAPPSERVICE_SUBSCRIPTIONID_XXXX }}
```
**Se connecte à Azure** par **OIDC** (fédération d'identité) : GitHub prouve son identité à Azure avec un jeton temporaire, sans stocker de mot de passe.
- `secrets.XXX` : des valeurs créées **automatiquement** par Azure dans ton dépôt, dans GitHub → **Settings** → **Secrets and variables** → **Actions**.
- Côté Azure, une **identité** (managée ou application Entra) a reçu le droit de déployer sur l'App Service.

```yaml
      - name: Deploy to Azure Web App
        id: deploy-to-webapp
        uses: azure/webapps-deploy@v3
        with:
          app-name: 'api-demo'
          slot-name: 'Production'
          package: .
```
**Envoie les fichiers sur l'App Service**, en zip deploy.
- `id` : donne un nom à l'étape, pour réutiliser ses sorties. C'est le cas de `webapp-url`, utilisée dans `environment.url`.
- `app-name` : le nom de ton App Service.
- `slot-name` : l'emplacement de déploiement. `Production` correspond au site principal.
- `package: .` : le dossier à déployer, ici le contenu de l'artifact téléchargé.

L'App Service redémarre ensuite avec la nouvelle version. Au démarrage, `Database.Migrate()` applique les éventuelles nouvelles migrations.

---

## 5. Variante : authentification par profil de publication

Si l'**authentification de base** est activée sur l'App Service, Azure peut générer le workflow avec un **profil de publication** au lieu d'OIDC. Dans ce cas, il n'y a pas d'étape `azure/login`, et le déploiement ressemble à ceci :

```yaml
      - name: Deploy to Azure Web App
        uses: azure/webapps-deploy@v3
        with:
          app-name: 'api-demo'
          slot-name: 'Production'
          publish-profile: ${{ secrets.AZUREAPPSERVICE_PUBLISHPROFILE_XXXX }}
          package: .
```

Le secret contient le fichier `.PublishSettings`, le même que celui importé dans Visual Studio.

| | OIDC (`azure/login`) | Profil de publication |
|---|---|---|
| Mot de passe stocké dans GitHub | Non, jeton temporaire | Oui, dans un secret |
| Authentification de base requise | Non | Oui |
| Recommandé par Microsoft | ✅ | Ancienne méthode |

---

## 6. Ce qui n'est PAS dans le workflow

- **La chaîne de connexion à la base** reste dans l'App Service (**Variables d'environnement** → `DefaultConnection`). Le workflow ne la connaît pas et ne l'écrase pas. Il ne faut **jamais** la mettre en clair dans le YAML.
- **La création des ressources Azure** (App Service, base SQL) : le workflow déploie du code sur des ressources qui existent déjà.
- **Les tests** : aucun par défaut (voir section 7).

---

## 7. Ajouter des tests au pipeline

Si un projet de tests existe dans la solution, ajoute une étape entre le build et le publish :

```yaml
      - name: Tests
        run: dotnet test --configuration Release --no-build
```
- `--no-build` réutilise la compilation de l'étape précédente.
- **Si un test échoue**, le job `build` échoue, et `deploy` ne se lance pas grâce à `needs: build`.

Avec plusieurs projets dans la solution, publie **uniquement l'API** :

```yaml
      - name: dotnet publish
        run: dotnet publish WebApplicationDemo/WebApplicationDemo.csproj -c Release -o "${{env.DOTNET_ROOT}}/myapp"
```

---

## 8. Suivre et déboguer une exécution

- **Onglet Actions** du dépôt GitHub : chaque exécution apparaît avec son statut (🟡 en cours, ✅ réussi, ❌ échoué). Clique sur un job, puis sur une étape, pour lire ses logs.
- **Côté Azure** : App Service → **Centre de déploiement** → **Journaux**.
- **Relancer** une exécution : bouton **Re-run jobs** sur le run, ou **Run workflow** grâce à `workflow_dispatch`.

| Erreur | Cause probable |
|---|---|
| Échec à l'étape `Build with dotnet` | Le code ne compile pas, ou la version de .NET du workflow ne correspond pas au `.csproj` |
| `Login to Azure` échoue (`AADSTS…`) | Secrets supprimés ou identité Azure supprimée : reconfigurer le Centre de déploiement |
| Déploiement réussi mais erreur **500.30** sur le site | Problème au démarrage de l'application, pas du workflow : chaîne de connexion, pare-feu SQL, runtime. Voir le **Flux de journaux** de l'App Service. |
| Push refusé après la configuration | Azure a commité le workflow : faire un `git pull` avant de pousser |

---

## 9. Résumé visuel

```
 git push master
       │
       ▼
┌─────────────── job: build (windows-latest) ───────────────┐
│ checkout → setup .NET 10 → build → (tests) → publish      │
│                                         │                 │
│                                upload artifact .net-app   │
└─────────────────────────────────────────┼─────────────────┘
                                          │  needs: build
┌─────────────── job: deploy (windows-latest) ──────────────┐
│ download artifact → login Azure (OIDC) → webapps-deploy   │
└─────────────────────────────────────────┼─────────────────┘
                                          ▼
                     App Service redémarre → Database.Migrate()
                                          ▼
                     https://api-demo.azurewebsites.net/api/todos
```
