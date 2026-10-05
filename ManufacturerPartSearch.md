# Manufacturer Part Search for AD17

An AD17-only prototype added to the EasyEDALoader extension. Open **Manufacturer Part Search** from its File/Tools menu entries, or run `EasyEDA-Loader:ManufacturerPartSearch`.

It calls AD17's native supplier manager (`EDP.Utils.GetSupplierManager`) and background supplier-search API. The provider dropdown lists enabled, visible suppliers configured in Altium; an Altium-named provider is preferred when present. It uses the host's normal provider configuration and authentication. No Nexar credentials or newer Altium DLLs are needed.

- Search by MPN or description, with paging and an in-stock option.
- Group offers by manufacturer and MPN; inspect stock, price breaks, technical parameters, and available datasheet/supplier URLs.
- Filter the current page by manufacturer or parameter/value, or copy parameters to the clipboard.
- Choose a matching component with Altium's library browser and copy it into `Documents/AltiumEE/ManufacturerParts.schlib`, adding manufacturer and supplier parameters. Existing components are not overwritten. Optional placement at the schematic origin uses Altium's library manager and places part 1 of multipart components.

CAD selection is manual. Available libraries and Vault content depend on the host setup. Component model links retain their original source or Vault identifiers; models are not downloaded into a new self-contained package. Declared manufacturer/MPN mismatches are rejected, and file-based models using the component library are resolved before copying. Keep source libraries available and review the imported symbol, pins, footprint, and model links.

AD17's Ciiva MPN endpoint fails for parts returned by its keyword search. For Ciiva, **MPN only** uses keyword search with an exact MPN filter on each page; paging remains available. Supplier-page links use URLs included in Ciiva's result parameters, avoiding its unreliable extra lookup.

This is an independent implementation of the workflow. It does not yet provide automatic manufacturer-to-Content-Vault model matching, cloud component acquisition, a docked panel, or the newer panel's global category/parametric filters. Altium documents the separate [parts-provider and Content Vault data sources](https://www.altium.com/documentation/altium-designer/components-libraries/searching-manufacturer-parts).

**Validation:** AD17 and AD26 builds pass. Under AD17.1/Wine, native Ciiva keyword search returns parts and technical parameters, and exact MPN filtering for `LM358N/NOPB` works. Supplier stock and price breaks display correctly. The standard-library `2N3904` was copied with onsemi/Avnet metadata and its TO-92A footprint reference, then placed and saved on a disposable schematic. Undo/Save removed it; Redo/Save restored exactly one component. Full exceptions are logged to `%LOCALAPPDATA%\EasyEDALoader\runtime-errors.log`.

Build/package commands are in [AD17.md](AD17.md). The AD17 archive includes this prototype; the AD26 build retains the existing loader.
