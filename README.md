# BatchOCR
BatchOCR er et program der kan batch-OCR-behandle TIF-dokumenter, baseret på open source OCR-motoren [Tesseract](https://github.com/tesseract-ocr/tesseract).

Det er lavet i C#/.NET Framework 4.7.2 med WPF og kan således kun afvikles på Windows.

![Screendump](../master/screenshot.png?raw=true)

Programmet paralleliserer OCR-behandlingen vha. .NET’s *Task Parallel Library*, så processorkraften udnyttes mest muligt på flerkernede maskiner. Programmet kalder Tesseract ved at starte kommandolinjeudgaven i ny proces. For at undgå at der oprettes for mange processer med dertilhørende overhead af multitrådningsprocessering er der indført en maksimalværdi for antal parallelle processer svarende til antallet af processorer på maskinen. Test viser at CPU stadig udnyttes 100 %, men at udførelsestiden bliver en smule kortere.

Programmet skaber en samlet SQLite-databasefil, hvor data fra hele kørslen ligger med en række pr. side (både fra enkelt- og flersidede TIF’er). Denne fil må viderebehandles eller tilgås vha. fx DB Manager for SQLite.

Tesseract kan OCR-behandle et utal af sprog ved installation af [sprogdatafiler](https://github.com/tesseract-ocr/tesseract/wiki/Data-Files)). Kun de danske sprogpakker er installeret i BatchOCR, og den vil derfor ikke kunne behandle fx engelsksprogede dokumenter optimalt out-of-the-box. Stikprøver tyder dog på der selv med dansk sprogdata, opnås en udmærket kvalitet for visse engelske tekster.

Afhængig af hvilket sprogdatasæt der bruges, kan man få hhv. højeste kvalitet eller hurtigste processering. Begge de danske sprogdatasæt er lagt som del af BatchOCR og man kan vælge mellem dem i GUI’en.

Når tesseract.exe kaldes fra programmet er der brugt følgende parametre (ud over angivelse af sti for TIF-fil, der skal behandles, og placering af resultat-tekstfilen):

* -c page_separator= en unik sideseparator-tekststreng (baseret på en GUID), så indholdet i resultatfilen kan nedbrydes til sideniveau.
* -c tessedit_do_invert=0, en parameter, der giver hurtigere udførelse, da billedet ikke testes i inverteret form. (Bruges kun ved hurtig udførelsen)
* --tesdata-dir PATH, hvor PATH svarer enten mappen tessdata-best eller tessdata-fast, afhængig af hvilken type OCR-behandling, der er valgt.

## Installation

For at køre programmet skal Tesseract være installeret på maskinen. Brug den seneste (64-bit) Windows-installer fra [Tesseract at UB Mannheim](https://github.com/UB-Mannheim/tesseract/wiki).

Stien til tesseract.exe (typisk C:\Program Files\Tesseract-OCR) skal registreres i Windows PATH-miljøvariabel, så BatchOCR kan kalde den.

BatchOCR skal muligvis afvikles i administrator-mode for at kunne få de nødvendige skriverettigheder.