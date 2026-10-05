# Manufacturer Part Search for AD17

An AD17-only prototype added to the EasyEDALoader extension. Open **Manufacturer Part Search** from its File/Tools menu entries, or run `EasyEDA-Loader:ManufacturerPartSearch`.

It calls AD17's native supplier manager (`EDP.Utils.GetSupplierManager`) and background supplier-search API. The provider dropdown lists enabled, visible suppliers configured in Altium; an Altium-named provider is preferred when present. It uses the host's normal provider configuration and authentication. No Nexar credentials or newer Altium DLLs are needed.

- Search by MPN or description, with paging and an in-stock option.
- Group offers by manufacturer and MPN; inspect stock, price breaks, technical parameters, and available datasheet/supplier URLs.
- Filter the current page by manufacturer or parameter/value, or copy parameters to the clipboard.
- Choose a matching component with Altium's library browser and copy it into `Documents/AltiumEE/ManufacturerParts.schlib`, adding manufacturer and supplier parameters. Existing components are not overwritten. Optional placement at the schematic origin uses Altium's library manager.

CAD selection is manual. Available libraries and Vault content depend on the host setup. Component model links retain their original source or Vault identifiers; models are not downloaded into a new self-contained package. Declared manufacturer/MPN mismatches are rejected, and file-based models using the component library are resolved before copying. Keep source libraries available and review the imported symbol, pins, footprint, and model links.

This is an independent implementation of the workflow. It does not yet provide automatic manufacturer-to-Content-Vault model matching, cloud component acquisition, a docked panel, or the newer panel's global category/parametric filters. Altium documents the separate [parts-provider and Content Vault data sources](https://www.altium.com/documentation/altium-designer/components-libraries/searching-manufacturer-parts).

**Validation:** AD17 and AD26 compilation only. No provider query, authentication attempt, UI execution, deployment, or tests have been run. The AD17 callback protocol, service compatibility, library-copy/model-link behavior, and placement require validation after the Wine host is ready. In particular, the presence of the old SDK interfaces does not establish that today's service accepts AD17 requests.

Build/package commands are in [AD17.md](AD17.md). The AD17 archive includes this prototype; the AD26 build retains the existing loader.
