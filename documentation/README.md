# TrayAppDotNET Documentation

The docs site is built with [Zensical](https://zensical.org/). Pages live in `docs/`. Navigation, theme and Markdown extensions live in `zensical.toml`.

## Building

Needs [uv](https://docs.astral.sh/uv/).

```sh
uv sync
uv run zensical serve --open
```

`zensical_serve.sh` does the same thing. `uv run zensical build` writes the static site to `site/`.

The PlantUML sources in `plantuml/` and its subfolders are separate from the site. Every diagram includes the shared `plantuml/dark-theme.puml` (`!include dark-theme.puml`, or `../dark-theme.puml` from a subfolder). `plantuml_serve.sh` compiles each diagram to an SVG beside its source and recompiles it when it or the theme changes; edit the `.puml` files, never the generated `.svg` files. `readme_images/` holds the images the repository README uses.
