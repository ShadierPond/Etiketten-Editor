# Etiketten Editor

Der **Etiketten Editor** ist eine Windows-Anwendung für deutsche
Einzelhandelsunternehmen. Mit ihr lassen sich Furnituren- und Produktkataloge
pflegen, Etiketten gestalten, Artikelbilder verwalten und Druckbögen für den
Arbeitsalltag erstellen.

Das Repository enthält zwei Oberflächen:

- die primäre **WPF-Desktop-Anwendung** unter `desktop/`
- die bestehende **Browser-Version** aus `index.html`, `style.css` und `main.js`

## Funktionen

- Artikel und Kategorien lokal verwalten
- Suche nach Beschreibung, BECO-Nummer, Referenz und Preisen
- Fotos pro Artikel auswählen und in der lokalen Mediathek wiederverwenden
- Quick-Service-Artikel mit QR-Code markieren
- Etikettenvorlage direkt in einer Vorschau bearbeiten
- Position, Größe, Schrift, Drehung und Ersatztext für fehlende BECO-Nummern
  konfigurieren
- Druckvorschau für eine Kategorie oder den gesamten Katalog
- PDF-Export und A4-Druck im 5×3-Etikettenraster
- Mitarbeiterkarten und QR-Codes pro Filiale verwalten
- JSON-Import und -Export einschließlich eingebetteter Fotos
- automatische lokale Speicherung mit SQLite
- Quick-Service-QR-Codes als ZIP exportieren
- öffentliche Updates aus den GitHub-Releases dieses Repositorys

## Desktop-Anwendung bauen

Voraussetzung ist das **.NET 10 SDK** unter Windows.

```powershell
dotnet build desktop\FuniturenEditor.Desktop.csproj
dotnet run --project desktop\FuniturenEditor.Desktop.csproj
```

Für eine portable x64-Version:

```powershell
.\desktop\publish-windows.ps1
```

Für den signierten Windows-Installer:

```powershell
.\desktop\build-installer.ps1
```

Der Installer wird pro Windows-Benutzer installiert. Die lokale Datenbank und
die Fotomediathek bleiben bei einer Deinstallation erhalten.

## Installer und Updates

Fertige Installer und Update-Pakete liegen im Bereich
[Releases](https://github.com/ShadierPond/Etiketten-Editor/releases):

- `FuniturenEditor-Setup-<Version>.exe` ist der Windows-Installer.
- `FuniturenEditor-win-x64.zip` ist das portable Update-Paket.

Die Desktop-Anwendung kann öffentliche Releases direkt prüfen. Eine GitHub-
Anmeldung ist dafür nicht erforderlich. Der Update-Download prüft zusätzlich
die von GitHub bereitgestellte SHA-256-Prüfsumme.

Die Installer werden mit einem Zertifikat von Tariq Alsalem signiert. Das
öffentliche Zertifikat liegt unter
`desktop\certificates\FuniturenEditor-Tariq-Alsalem.cer`; der private Schlüssel
wird nicht im Repository gespeichert.

## Daten und Sicherheit

Die Desktop-Anwendung speichert Daten lokal im Benutzerprofil:

- Datenbank: `%LOCALAPPDATA%\ShadierPond\FuniturenEditor\funituren-desktop.db`
- Fotos: `%LOCALAPPDATA%\ShadierPond\FuniturenEditor\Photos`

Die Browser-Version ist eine separate statische Oberfläche. Sie verwendet
keine lokale SQLite-Datenbank der Desktop-Anwendung.

## Probleme melden

Wenn etwas nicht funktioniert, kann im Tab
[Issues](https://github.com/ShadierPond/Etiketten-Editor/issues) ein Problem
gemeldet werden. Bitte möglichst angeben:

- verwendete Version
- Windows-Version
- genaue Schritte zur Reproduktion
- erwartetes und tatsächliches Verhalten
- relevante Fehlermeldungen oder Screenshots

## Lizenz

Dieses Projekt steht unter der **Etiketten-Editor Source-Available License**.
Die vollständigen Bedingungen stehen in [LICENSE](LICENSE).

Die Lizenz erlaubt, den Quellcode selbst zu bauen und die bereitgestellten
Installer und Update-Pakete intern zu verwenden. Das Programm, modifizierte
Versionen, Installer und Updates dürfen jedoch nicht weiterveröffentlicht,
verkauft, vermietet, verteilt oder als eigener Dienst angeboten werden.

Diese Lizenz ist bewusst keine Open-Source-Lizenz im Sinne der OSI-Kriterien.

## Verzeichnisübersicht

| Pfad | Inhalt |
| --- | --- |
| `desktop/` | Primäre WPF-Anwendung, Build- und Installer-Skripte |
| `desktop/FuniturenEditor.WinUI/` | Separater WinUI-3-Versuch |
| `Icons/` | Verwendete SVG-Symbole |
| `index.html`, `style.css`, `main.js` | Browser-Version |

Entwickelt von **ShadierPond** / **Tariq Alsalem**.
