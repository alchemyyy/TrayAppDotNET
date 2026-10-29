# TrayAppDotNET Documentation

The docs site is built with [Zensical](https://zensical.org/). Pages live in `docs/`. Navigation, theme and Markdown extensions live in `zensical.toml`.

## Building

Needs [uv](https://docs.astral.sh/uv/).

```sh
uv sync
uv run zensical serve --open
```

`zensical_serve.sh` does the same thing. `uv run zensical build` writes the static site to `site/`.

The PlantUML sources in `plantuml/` are separate from the site. `plantuml_serve.sh` recompiles them as they change. `readme_images/` holds the images the repository README uses.
