# Erreur de build MC1000 — Incompatibilité SDK .NET 9 + WPF .NET 4.8

## Symptôme

```
Microsoft.WinFX.targets(211,9): error MC1000:
Could not find type 'System.Security.SecurityRuleSet'
in assembly 'System.Runtime.dll' (.NETFramework\v4.8\Facades\)
```

## Cause

Le projet cible `.NET Framework 4.8` avec `UseWPF=true`.  
Quand on compile avec **`dotnet build`** (SDK .NET 9), MSBuild tourne sous runtime Core.  
Les cibles WPF (`Microsoft.WinFX.targets`) choisissent alors `net9.0/PresentationBuildTasks.dll`
comme compilateur BAML (étape `MarkupCompilePass1`).

Ce compilateur BAML en .NET 9 charge les assemblies de référence .NET 4.8 pour analyser
les types utilisés dans le XAML. Il tente de charger la facade `.NETFramework\v4.8\Facades\System.Runtime.dll`
(datant de 2019) et cherche le type `System.Security.SecurityRuleSet` — type qui **n'existe pas**
dans cette version de la facade (il est dans `mscorlib.dll`, non forwardé dans cette facade).

## Pourquoi ça marchait avant

Les builds précédentes (avant 15:42 ce jour) utilisaient un **BAML en cache** (`obj/Release/net48/`).
MSBuild saute `MarkupCompilePass1` si le XAML n'a pas changé.  
La modification du `AiPalette.xaml` (boutons ✕/⚙) a invalidé le cache → première recompilation XAML → crash.

## Solutions possibles

### Option A — Installer le targeting pack .NET 4.8.1 ✅ recommandé

Le .NET 4.8.1 Developer Pack met à jour les facades de référence.
La facade `System.Runtime.dll` v4.8.1 inclut le forwarding de `SecurityRuleSet`.

Télécharger sur : https://dotnet.microsoft.com/download/dotnet-framework/net481

Après installation → `.\build.ps1 -UniqueOutDir` fonctionnera directement.

### Option B — Installer le SDK .NET 8 en parallèle

Le bug MC1000 n'existe pas dans le SDK .NET 8. Avec un `global.json` à la racine du projet :

```json
{
  "sdk": { "version": "8.0.0", "rollForward": "latestMinor" }
}
```

→ `dotnet build` utiliserait SDK 8.x → `PresentationBuildTasks.dll` net8.0 → pas de bug.

Télécharger le SDK 8.x sur : https://dotnet.microsoft.com/download/dotnet/8.0

### Option C — Compiler depuis Visual Studio 2022 (sans rien installer)

VS 2022 est déjà installé. Ouvrir le `.csproj` directement dans VS → Build → fonctionne.
VS utilise MSBuild sous runtime .NET Framework → `net472/PresentationBuildTasks.dll`
→ pas de bug du SDK .NET 9.

**Limitation**: la variable d'environnement `ACAD_DLLS` doit être définie dans les variables
système (pas juste dans cmd/PowerShell) pour que VS la lise.
Ou : éditer le `.csproj` pour mettre le chemin en dur.

### Option D — Patch local (sans installation, sans VS)

Copier la `System.Runtime.dll` depuis le SDK .NET 9 pour remplacer la facade .NET 4.8 :

```powershell
$src = "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\9.0.5\ref\net9.0\System.Runtime.dll"
$dst = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8\Facades\System.Runtime.dll"
# Sauvegarder l'original
Copy-Item $dst "$dst.bak"
# Remplacer
Copy-Item $src $dst
```

⚠️ Risqué — peut casser d'autres projets. À éviter sauf si les autres options ne marchent pas.

## Recommandation

**Option A** (targeting pack .NET 4.8.1) est la plus propre et sans effet de bord.
Si tu ne veux pas installer : utilise **Option C** (VS 2022) en définissant `ACAD_DLLS` dans les
variables d'environnement système (Panneau de configuration → Variables d'environnement).
