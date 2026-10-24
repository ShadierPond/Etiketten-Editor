# Furnituren Editor

Der Furnituren Editor ist eine lokale Desktop-Anwendung zur Verwaltung von
Furnituren-Katalogen und Mitarbeitenden. Die empfohlene Windows-Desktop-Version
basiert auf **WPF** und bietet direkte Kontrolle über Druckvorschau und
Oberfläche. Eine WinUI-3-Version bleibt als separater Versuch im Repository,
wird derzeit aber nicht als primäre Anwendung empfohlen. Die öffentliche
GitHub-Pages-Version gibt es zusätzlich weiterhin.

## Windows-Desktop-Anwendung (WPF)

Voraussetzung: .NET 10 SDK.

Im Repository-Ordner bauen:

```powershell
dotnet build desktop\FuniturenEditor.Desktop.csproj
```

Die Anwendung starten:

```powershell
dotnet run --project desktop\FuniturenEditor.Desktop.csproj
```

Beim ersten Start wird ein Administratorkonto angelegt. Weitere lokale
Benutzerkonten kann danach ein Administrator in der App verwalten. Es gibt
keine Passwort-Mindestlänge oder Komplexitätsvorgabe. Passwörter werden als
PBKDF2-Hashes gespeichert; die Ersteinrichtung zeigt eine farbige
Stärkeanzeige als unverbindlichen Hinweis. Nach fünf falschen Anmeldeversuchen
wird ein Konto für 15 Minuten gesperrt.

Die SQLite-Datenbank und die lokale Fotomediathek behalten dieselben Pfade wie
die bisherige Desktop-App:

- Datenbank: `%LOCALAPPDATA%\ShadierPond\FuniturenEditor\funituren-desktop.db`
- Fotos: `%LOCALAPPDATA%\ShadierPond\FuniturenEditor\Photos`

### Funktionen

- lokale Anmeldung, Ersteinrichtung und Benutzerverwaltung
- mehrere Furnituren-Kategorien mit jeweils 15 bearbeitbaren Positionen
- automatische SQLite-Speicherung, Suche über Katalogdaten und
  Positionsänderungen per Nummerneingabe, Verschiebeschaltflächen oder Ziehen
  an der Positionsnummer (Pos. ↕); die Positionsnummern werden dabei lückenlos
  neu vergeben und Vorschau sowie Druck übernehmen die neue Reihenfolge
- einzelne Artikel lassen sich löschen; nachfolgende Positionen rücken nach
  und ein leerer Platz hält den 15-Etiketten-Druckbogen vollständig. Wird die
  letzte Kategorie gelöscht, wird sie durch eine neue leere Kategorie ersetzt
- Etikettenvorlagen direkt in der Vorschau live verschieben und an den Kanten-
  und Eckgriffen skalieren, Drehwinkel präzise einstellen sowie QR-Code oder Code-128-Barcode je Artikel
  auswählen; Quick-Service-Artikel bleiben QR-Codes
- Artikelliste mit Einkaufspreis, Kassenpreis, eigenem Verkaufspreis und
  empfohlenem Preis; der empfohlene Preis wird standardmäßig aus dem
  Einkaufspreis × 2 berechnet und kann manuell überschrieben werden. Wird der
  manuelle Wert geleert, wird die Empfehlung wieder automatisch berechnet.
  Vorschau und Liste sind miteinander verknüpft und heben den ausgewählten
  Artikel kurz hervor
- Artikelbilder mit Miniaturansicht in der Liste; Fotos werden lokal und
  inhaltsbasiert dedupliziert und lassen sich aus der gemeinsamen Mediathek
  wiederverwenden. Fotos können aus der Mediathek gelöscht werden; falls
  Artikel das Foto verwenden, werden diese vorab angezeigt und ihre
  Fotozuordnungen nach Bestätigung geleert
- JSON-Import und -Export; beim Export werden verfügbare Fotos eingebettet.
  Beim Import bleiben Katalogdaten erhalten, wenn einzelne Fotos ein nicht
  unterstütztes Format haben; diese Fotozuordnungen werden mit Warnhinweis
  ausgelassen
- Mitarbeiterlisten pro Filiale mit bearbeitbaren Namen, Nummern und QR-Codes
- gemeinsames, pro Katalog gespeichertes Etikettenlayout mit Etikettengröße und
  Position, Größe, Schriftgrad und Schriftschnitt für Foto, QR, Beschreibung,
  BECO, Referenz, Verkaufs- und Einkaufspreis
- Quick-Service-QR-Codes als ZIP-Datei
- druckoptimierte DIN-A4-Ausgabe: aktuelle oder alle Etikettenkategorien im
  5×3-Raster (Querformat) sowie aktuelle oder alle Mitarbeiterfilialen
  (Hochformat)

Vor dem Windows-Druckdialog öffnet die WPF-Version eine eigene Vorschau des
tatsächlichen DIN-A4-Druckdokuments. Von dort kann der Druckdialog geöffnet
oder der Vorgang abgebrochen werden. Es wird kein externes Browserfenster
benötigt. Vor dem Drucken Papierformat, Ausrichtung und Maßstab kontrollieren.

Eine portable x64-Version kann mit dem bestehenden Skript erstellt werden:

```powershell
.\desktop\publish-windows.ps1
```

### Private Softwareupdates

Die WPF-App kann optional über **Nach Updates suchen** mit der GitHub-App
„Funituren Editor Updater“ auf das private Repository
`ShadierPond/Funituren-Editor-Updates` zugreifen. Die Anmeldung nutzt GitHub
Device Flow; das Zugriffstoken wird mit Windows-DPAPI für das aktuelle
Windows-Konto geschützt gespeichert. Ohne Anmeldung funktionieren die
lokalen App-Funktionen weiter. Es wird kein Token im Quellcode oder im
Installationspaket abgelegt.

Für ein Update die Programmversion in
`desktop/FuniturenEditor.Desktop.csproj` erhöhen, danach
`.\desktop\publish-windows.ps1` ausführen und
`desktop\publish\FuniturenEditor-win-x64.zip` als Asset eines neuen privaten
GitHub-Releases hochladen. Der Release-Tag muss `v` plus die Programmversion
verwenden (zum Beispiel `v1.0.1`). Beim Prüfen wird das neueste Release samt
Paket geladen und eine optionale von GitHub bereitgestellte SHA-256-Prüfsumme
validiert. Zum Installieren wird die App geschlossen, die ZIP-Datei in den
Programmordner entpackt und die Anwendung danach neu gestartet. Es gibt noch
kein Update-Release; bis eines veröffentlicht ist, kann die App daher noch
keine Update-Version herunterladen.

Die optionale WinUI-3-Version liegt unter `desktop/FuniturenEditor.WinUI/`.
Beide Desktop-Varianten verwenden dieselbe Datenbank und Fotomediathek. Nicht
gleichzeitig mit beiden Apps dieselben Daten parallel bearbeiten.

### Private Code-Signatur

Release-Pakete der WPF-Version werden mit dem selbstsignierten Zertifikat
**Tariq Alsalem** signiert. Der private Schlüssel ist nicht exportierbar und
liegt ausschließlich im persönlichen Windows-Zertifikatsspeicher des
Erstellers (`Cert:\CurrentUser\My`). Er darf nie in Git, ein Update-ZIP oder
eine Sicherung kopiert werden.

Das öffentliche Zertifikat
`desktop\certificates\FuniturenEditor-Tariq-Alsalem.cer` ist für den späteren
Installer bestimmt. Vor der ersten Installation muss der Installer es nach
ausdrücklicher Zustimmung des Benutzers in dessen Speicher für
**Vertrauenswürdige Stammzertifizierungsstellen** und
**Vertrauenswürdige Herausgeber** importieren. Dadurch kann Windows die
selbstsignierte Signatur dieses privaten Projekts prüfen. Microsoft SmartScreen
bewertet Download-Reputation unabhängig von der Signatur und kann deshalb
trotzdem einen Hinweis anzeigen.

### Windows-Installer

Der Installer wird mit Inno Setup erzeugt:

```powershell
.\desktop\build-installer.ps1
```

Er installiert die Anwendung ausschließlich für das aktuelle Windows-Konto
unter `%LOCALAPPDATA%\Programs\Furnituren Editor`, erstellt einen
Startmenüeintrag und bietet eine abwählbare Desktop-Verknüpfung an. Die lokale
SQLite-Datenbank und Fotomediathek bleiben beim Deinstallieren erhalten.

Zusätzlich enthält der Installer eine **standardmäßig nicht aktivierte** Option,
das öffentliche Zertifikat von Tariq Alsalem im Benutzerkonto als
vertrauenswürdige Stammzertifizierungsstelle und als vertrauenswürdigen
Herausgeber zu hinterlegen. Diese Option darf nur ausgewählt werden, wenn der
Installer aus der vertrauenswürdigen privaten Quelle stammt. Sie ist für die
Prüfung der selbstsignierten Programmsignatur erforderlich, aber nicht für die
Installation selbst.

Für die Diagnose von Vorschau-Einfrierern gibt es einen internen Build:

```powershell
dotnet build desktop\FuniturenEditor.Desktop.csproj -c InternalDebug
```

Die Diagnoseversion schreibt Vorschau-Aufbau, Auswahl, Zoom, Speicherdauer,
Ausnahmen und längere UI-Thread-Blockaden nach
`%LOCALAPPDATA%\ShadierPond\FuniturenEditor\Diagnostics`. Es werden dabei
keine Artikeltexte oder Preise protokolliert. Die Diagnoseversion und die
normale Desktop-App nicht gleichzeitig verwenden, da beide dieselbe lokale
Datenbank nutzen.

## Online verwenden

Die statische GitHub-Pages-Version ist weiterhin öffentlich verfügbar:
<https://shadierpond.github.io/Funituren-Editor/>

Sie benötigt einen Browser und bietet keine Anmeldung. Die lokale Anmeldung
und Datenbank der Windows-Anwendungen schützen diese öffentliche Website nicht.
Die Browser-Version bietet weiterhin unter anderem Kategorien, Etiketten,
Mitarbeiterlisten, Suche, Touch-Bedienung, Druckvorschau und JSON-Import/-Export.

## Dateien

| Datei | Aufgabe |
| --- | --- |
| `desktop/FuniturenEditor.Desktop.csproj` | Primäre WPF-Desktop-Anwendung |
| `desktop/CatalogData.cs` | Gemeinsames Katalog-Datenmodell |
| `desktop/LocalStore.cs` | Gemeinsame SQLite-Datenbank und Anmeldung |
| `desktop/FuniturenEditor.WinUI/` | Optionale WinUI-3-Version |
| `index.html`, `style.css`, `main.js` | Öffentliche statische Browser-Version |

---

Entwickelt von **ShadierPond** aka. **Tariq Alsalem**
