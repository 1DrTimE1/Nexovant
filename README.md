# Nexovant

Säker diskrensning för Windows. Nexovant skannar kända cache- och temp-mappar och låter dig välja exakt vilka filer som ska tas bort.

## Bygga

```
dotnet build -c Release
```

## Släppa en ny version (autouppdatering)

Appen kollar `https://api.github.com/repos/1DrTimE1/Nexovant/releases/latest` vid start. Om det finns en nyare version visas en knapp "Uppdatera nu".

1. Committa dina ändringar.
2. Skapa och pusha en versionstagg:
   ```
   git tag v1.0.1
   git push origin v1.0.1
   ```
3. GitHub Actions bygger `Nexovant.exe` och skapar en release automatiskt. Alla som kör en äldre version får erbjudande om att uppdatera.

Repot måste vara **publikt** för att appen ska kunna läsa releaser utan inloggning. Byt repo i `Core/Updater.cs` (`Repository`) om det ska heta något annat.
