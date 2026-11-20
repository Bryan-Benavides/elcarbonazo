"""Valida las fuentes del catálogo de defensa sin conectarse a PostgreSQL."""

import csv
import unicodedata
from collections import Counter
from decimal import Decimal, InvalidOperation
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent / "specs" / "entrega-defensa-local"
MAX_STOCK = Decimal("99999999.9999")


def read_csv(name, columns):
    with (ROOT / name).open(encoding="utf-8", newline="") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != columns:
            raise ValueError(f"{name}: columnas incorrectas")
        return list(reader)


def valid_text(value, limit):
    return (
        value == value.strip()
        and value == unicodedata.normalize("NFC", value)
        and 0 < len(value) <= limit
        and not any(unicodedata.category(char) in {"Cc", "Cs"} for char in value)
    )


def amount(value, maximum, scale, allow_zero=False):
    try:
        parsed = Decimal(value)
        if not parsed.is_finite() or parsed < 0 or (not allow_zero and parsed == 0):
            raise ValueError
        if parsed > maximum or parsed.as_tuple().exponent < -scale:
            raise ValueError
        return parsed
    except (InvalidOperation, ValueError):
        raise ValueError(f"cantidad inválida: {value!r}") from None


def main():
    products = read_csv(
        "catalogo-productos.csv", ["numero", "nombre", "categoria", "precio_nio"]
    )
    ingredients = read_csv(
        "catalogo-ingredientes.csv", ["nombre", "unidad", "stock_inicial", "stock_minimo"]
    )
    if len(products) != 90 or [p["numero"] for p in products] != [
        str(number) for number in range(1, 91)
    ]:
        raise ValueError("Se esperan 90 productos numerados del 1 al 90")
    if len({p["nombre"] for p in products}) != len(products):
        raise ValueError("Hay productos duplicados")
    for product in products:
        if not valid_text(product["nombre"], 120):
            raise ValueError(f"Nombre de producto inválido: {product['numero']}")
        if product["categoria"] not in {"Comida", "Bebida", "Licor"}:
            raise ValueError(f"Categoría inválida: {product['numero']}")
        amount(product["precio_nio"], Decimal("999999.99"), 2)

    by_name = {}
    keys = set()
    for ingredient in ingredients:
        name = ingredient["nombre"]
        key = unicodedata.normalize("NFC", name.lower())
        if not valid_text(name, 120) or key in keys:
            raise ValueError(f"Ingrediente inválido o duplicado: {name}")
        if ingredient["unidad"] not in {"lb", "kg", "oz", "g", "unidad", "ml", "lt"}:
            raise ValueError(f"Unidad inválida: {name}")
        stock = amount(ingredient["stock_inicial"], MAX_STOCK, 4, allow_zero=True)
        amount(ingredient["stock_minimo"], MAX_STOCK, 4, allow_zero=True)
        by_name[name] = stock
        keys.add(key)

    recipes = {}
    total_lines = 0
    for line_number, line in enumerate(
        (ROOT / "recetas.txt").read_text(encoding="utf-8").splitlines(), 1
    ):
        if not line or line.startswith("#"):
            continue
        try:
            number, entries = line.split("|", 1)
            product_number = int(number)
            if number != str(product_number) or product_number in recipes:
                raise ValueError("número duplicado o inválido")
            parts = [entry.rsplit("=", 1) for entry in entries.split(";")]
            if not 1 <= len(parts) <= 50:
                raise ValueError("cantidad de ingredientes inválida")
            names = set()
            for name, value in parts:
                if name not in by_name or name in names:
                    raise ValueError(f"ingrediente inexistente o duplicado: {name}")
                quantity = amount(value, MAX_STOCK, 4)
                if quantity > by_name[name]:
                    raise ValueError(f"stock inicial insuficiente para una porción: {name}")
                names.add(name)
            recipes[product_number] = names
            total_lines += len(parts)
        except (ValueError, TypeError) as error:
            raise ValueError(f"recetas.txt línea {line_number}: {error}") from error
    if set(recipes) != set(range(1, 91)):
        raise ValueError("Se requiere exactamente una receta por producto")
    print(f"Productos: {len(products)}; ingredientes: {len(ingredients)}; "
          f"recetas: {len(recipes)}; líneas: {total_lines}.")
    print("Categorías:", dict(Counter(p["categoria"] for p in products)))
    print("Stock suficiente para una porción de cada receta por separado; "
          "no se comprobó la preparación simultánea de todas.")


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, csv.Error) as error:
        raise SystemExit(f"Validación fallida: {error}") from error
