# Furnituren Editor

Ein browserbasierter, statischer Editor für druckfertige Furnituren-Etiketten
und QR-Code-Karten für Mitarbeitende. Er benötigt keinen Server und keine
Datenbank: `index.html` kann direkt im Browser geöffnet werden.

## Funktionen

### Furnituren-Etiketten

- Kategorien für eigene Druckbögen anlegen, umbenennen und löschen
- 15 Etiketten pro DIN-A4-Bogen im Querformat bearbeiten
- Beschreibung, Beco, Referenz, Einkaufs- und Verkaufspreis pflegen
- Quick-Service-Etiketten ohne offiziellen QR-Code markieren
- QR-Codes automatisch aus der Referenz erzeugen
- Fotos in eine lokale Medienbibliothek laden, zuordnen oder entfernen
- Etiketten per Drag & Drop sortieren
- Aktuelle Kategorie oder alle Kategorien drucken

### Mitarbeitercodes

- Eigene Filialen als getrennte Mitarbeiterlisten anlegen
- QR-Code-Karten mit Name, Mitarbeitendennummer und Anmeldecode erstellen
- Mitarbeitende pro Filiale bearbeiten, löschen und per Drag & Drop sortieren
- Kartenmaße, QR-Code-Position, Namensgröße, Nummerngröße und Fettdruck
  global einstellen
- Lange Namen auf maximal 42 Zeichen begrenzen, damit Karten lesbar bleiben
- Aktuelle Filiale oder alle Filialen auf separaten DIN-A4-Blättern im
  Hochformat drucken

## Verwendung

1. `index.html` im Browser öffnen.
2. Links zwischen **Furnituren** und **Mitarbeiter** wechseln.
3. Kategorien beziehungsweise Filialen anlegen und die Einträge bearbeiten.
4. Mit **Exportieren** eine Sicherung als JSON-Datei speichern.
5. Mit **Importieren** eine zuvor gespeicherte JSON-Datei wieder laden.

Ältere JSON-Dateien, die nur Furnituren-Kategorien enthalten, bleiben
importierbar. Fehlende Mitarbeiterlisten und Karteneinstellungen werden dabei
automatisch ergänzt.

## Dateien

| Datei | Aufgabe |
| --- | --- |
| `index.html` | Oberfläche und Dialoge |
| `style.css` | Bildschirm- und Drucklayout |
| `main.js` | Lokale Datenverwaltung, QR-Codes, Import/Export und Interaktionen |

## Hinweis zur Datenspeicherung

Die Daten werden während der geöffneten Browser-Sitzung gehalten. Speichern Sie
Ihre Arbeit regelmäßig über **Exportieren**, damit sie dauerhaft als JSON-Datei
gesichert ist.

---

Entwickelt von **ShadierPond** aka. **Tariq Alsalem**
