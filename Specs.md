# Chiro Project Specification

## 1. Objective

Simplify the drafting of ecological diagnostics by automating data collection regarding conservation zones (ZNIEFF, Natura 2000, local red lists) and the regulatory/conservation statuses of species.

This is separated into two main sub-projects:

- **Project 1:** Automated zone and species data extraction based on an input study perimeter.
- **Project 2:** Automation of reference data updates for existing Excel tools (filling out an updated "empty" template from INPN, IUCN, and local red lists).

## 2. Platform & Usage

- **Format:** Desktop environment only. Standalone software.
- **Preview:** A minimal visual map preview in the browser is required to verify correct positioning.

## 3. Inputs

- **Study Perimeter:** Provided by the user. Formats: `.shp` (QGIS), optionally `.kml` or `.gpx` or `.csv`. A point with a radius.
- **Local Red Lists:** Provided by the user as `.pdf` or `.xls`.
- **External Data Sources:**
  - **INPN (National Inventory of Natural Heritage):** For species statuses, ZNIEFF, and Natura 2000 Standard Data Forms (FSD). `.shp` files from INPN should be used for accurate geographical boundaries instead of single coordinate points.
  - **IUCN:** For red list statuses (World, Europe, France, Region, optionally Department).
  - Specific protection statuses: France strictly, and Habitat Directive.

## 4. Features & Data Processing

- **Search Perimeter:** From the closest point of the project (not center), up to 20 km maximum, with selectable step intervals (5, 10, 15, and 20 km).
- **Zone Identification:** Determine the situation of the project relative to zoning areas, including:
  - Zone codes and names.
  - Distance and orientation from the project.
  - Direct links to the respective INPN fact sheets.
- **Species Identification Details:**
  - List of species in each zone (via INPN FSD).
  - Retrieve statuses based on INPN.
  - Important info: Year of last observation.
  - Display rules (Enjeux): Rules will be provided by "Maël".
- **Data Organization (in output tables):**
  - Incremental sorting and filtering across all columns (grouped by type, then prioritized by distance).
  - Species filtering by type and alphabetical sorting by vernacular name (fallback to scientific name).

## 5. Outputs

- **Tabular Data:** Excel workbook with multiple sheets, or multiple single-sheet CSV/XLSX files containing the inventory tables.
- **Cartography:** QGIS format (`.shp`) for the geographic output. No PDF/PNG exports are needed, as maps will be manually post-processed.
- **Updated Template (for Project 2):** Generation of a downloaded, updated empty template to be manually filled by the client in Excel. (Based on templates like `FV_RHONE-ALPES_Saisie_2021` and `modele_SINP_Faune_vertebree`).

## 6. Technical Stack

- **Backend / Desktop Host:** Photino.NET (C# .NET 10.0) for a lightweight, native cross-platform desktop application wrapping the robust backend logic. Entity Framework Core (Code-First) coupled with NetTopologySuite will be used for ORM and spatial queries.
- **Database:** SQLite with the SpatiaLite extension, running locally as a single file, providing lightweight, zero-configuration GIS functionality.
- **Frontend / UI:** Vue.js 3, utilizing Vuetify for rapid, clean material design components, and Leaflet for the web map preview module, embedded within the Photino Webview.
