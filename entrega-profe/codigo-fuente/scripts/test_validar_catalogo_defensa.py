"""Pruebas sin base de datos para las fuentes del catálogo de defensa."""

import contextlib
import importlib.util
import io
import shutil
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name("validar-catalogo-defensa.py")
SPEC = importlib.util.spec_from_file_location("catalogo_defensa", SCRIPT)
validator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(validator)


class CatalogoDefensaTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.original_root = validator.ROOT
        self.addCleanup(setattr, validator, "ROOT", self.original_root)
        validator.ROOT = Path(self.temp.name)
        for name in ("catalogo-productos.csv", "catalogo-ingredientes.csv", "recetas.txt"):
            shutil.copyfile(self.original_root / name, validator.ROOT / name)

    def validate(self):
        with contextlib.redirect_stdout(io.StringIO()):
            validator.main()

    def change(self, filename, before, after):
        path = validator.ROOT / filename
        content = path.read_text(encoding="utf-8")
        self.assertIn(before, content)
        path.write_text(content.replace(before, after, 1), encoding="utf-8")

    def test_fuentes_completas(self):
        self.validate()

    def test_rechaza_ingrediente_inexistente(self):
        self.change("recetas.txt", "Arroz blanco=150", "Ingrediente inexistente=150")
        with self.assertRaises(ValueError):
            self.validate()

    def test_rechaza_duplicado_en_receta(self):
        self.change("recetas.txt", "Arroz blanco=150;", "Arroz blanco=150;Arroz blanco=1;")
        with self.assertRaises(ValueError):
            self.validate()

    def test_rechaza_precio_no_numerico(self):
        self.change("catalogo-productos.csv", "Comida,60.00", "Comida,NaN")
        with self.assertRaises(ValueError):
            self.validate()

    def test_rechaza_unidad_incompatible(self):
        self.change("catalogo-ingredientes.csv", "Mantequilla,g,4000", "Mantequilla,taza,4000")
        with self.assertRaises(ValueError):
            self.validate()

    def test_rechaza_stock_insuficiente(self):
        self.change("catalogo-ingredientes.csv", "Mantequilla,g,4000", "Mantequilla,g,1")
        with self.assertRaises(ValueError):
            self.validate()


if __name__ == "__main__":
    unittest.main()
