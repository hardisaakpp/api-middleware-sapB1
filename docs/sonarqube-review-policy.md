# Política de revisión de hallazgos de SonarQube

Contrato para resolver issues de SonarQube en este repositorio. Aplica tanto a Claude Code como a cualquier persona del equipo que corrija issues de Sonar.

> Adaptado de `mba3-consultas/docs/sonarqube-review-policy.md` (fuente original, donde también
> se documentan los patrones recurrentes de otros repos recurrentes: `scnexus-front`,
> `scnexus-core`). Al encontrar aquí un patrón nuevo que se repita, documentarlo en la sección 6
> de este archivo; si el patrón es genérico de C#/.NET (no específico de este middleware),
> considerar avisarlo también en `mba3-consultas` y en `scnexus-core` para que ambos se beneficien.

## 1. Alcance permitido (lo único que se puede tocar)

- Solo se aplican cambios que correspondan a un **issue concreto reportado por SonarQube** (Code Smell, Bug, Vulnerability, Security Hotspot o Duplication), citando la regla exacta (ej. `csharpsquid:S6960`, `external_roslyn:CA1859`, etc.).
- Cambios típicos permitidos: reducir complejidad ciclomática, eliminar código muerto/no usado, renombrar variables poco claras, eliminar duplicación, corregir *code smells* de estilo, ajustar manejo de recursos (`using`/`IDisposable`, cierres de conexiones), simplificar condicionales sin cambiar su resultado lógico.
- Nada que no tenga un issue asociado se toca, aunque "de paso" se vea algo mejorable.

## 2. Prohibiciones explícitas

- **Nunca modificar lógica de negocio**: reglas de cálculo, condiciones que afecten el resultado, validaciones de negocio, umbrales, flujos de decisión.
- **Nunca cambiar el comportamiento observable**: mismo input debe producir el mismo output antes y después del cambio.
- **Nunca modificar firmas públicas** (interfaces, endpoints, contratos de request/response) sin aprobación explícita del usuario — recordar que este middleware tiene consumidores externos documentados en `docs/CONTRATO_MIDDLEWARE.md`.
- **Nunca alterar tests para que "pasen"** ocultando el problema real (cambiar aserciones, comentar tests que fallan, aumentar timeouts para maquillar un fallo).
- **Nunca tocar configuración de infraestructura/despliegue** (CI/CD, variables de entorno, Dockerfiles) salvo que el issue sea explícitamente sobre eso.
- **No borrar código "por sospecha de no uso"** sin verificar referencias reales en todo el repo.

## 3. Zona gris — detenerse y preguntar antes de aplicar

Hay reglas de Sonar que *parecen* de estilo pero pueden cambiar comportamiento. Requieren aprobación explícita antes de aplicarse:

- Cambios en manejo de excepciones (`try/catch`, tipos de excepción capturados).
- Cambios en comparaciones (identidad vs igualdad, coerciones de tipo).
- Reordenar condiciones con evaluación de cortocircuito.
- Cualquier "Security Hotspot" que implique cambiar validación de entradas o lógica de autenticación/autorización.
- Cualquier refactor que cruce más de un módulo o límites de capas (ej. de controller a use case).

## 4. Cuándo NO modificar código (issues cosméticos sin impacto funcional)

Si el hallazgo es puramente de estilo/convención y corregirlo:

- no aporta ningún beneficio funcional, y
- requiere tocar código que funciona intencionalmente así, o implica un cambio innecesariamente amplio (muchos callers, muchos archivos),

→ no modificar el código. En su lugar, indicar cómo silenciarlo desde la interfaz de SonarQube (marcar como "Won't Fix" / "False Positive") o ajustando la configuración del análisis de Sonar, explicando el motivo.

## 5. Proceso de trabajo obligatorio

1. Un issue de SonarQube = un cambio, en un commit separado y descriptivo (`fix(sonar): S6960 - dividir WarehousesController`).
2. Ejecutar los tests existentes (`dotnet test`) **antes y después** de cada cambio. Si no hay tests para ese código, avisar antes de tocarlo.
3. Trabajar siempre en una rama separada (nunca directo a `main`); el usuario revisa el diff antes de mergear.
4. Documentar cada cambio: regla resuelta, archivo, línea, y una frase de qué se hizo.

## 6. Patrones recurrentes en este repo (.NET 8 / C#)

Hallazgos del análisis de Sonar sobre `api-middleware-sap`, PR 395 (2026-08-31):

- **`external_roslyn:CA1859`** (cambiar el tipo de un parámetro de una interfaz de colección, ej. `IReadOnlyDictionary<K,V>`, a la clase concreta, ej. `Dictionary<K,V>`, "for improved performance" — evita la indirección de la interfaz, permite devirtualizar la llamada): **solo aplicar sin preguntar cuando el método es `private` (o `static` privado) Y tiene un único caller Y ese caller ya construye/tiene un `Dictionary` concreto** (no otra implementación de `IReadOnlyDictionary`, como `ImmutableDictionary` o `.AsReadOnly()`) — verificar los tres con `grep -n "NombreMetodo("` antes de tocar la firma. Si el método es `public`/`internal` con más de un caller, es un cambio de firma (zona gris, sección 3) que requiere confirmar que TODOS los callers ya pasan el tipo concreto. Ejemplo: `ValidatePurchaseOrderUseCase.MapItem(itemData, itemErrors)`.
- **`csharpsquid:S6960`** ("This controller has multiple responsibilities and could be split into N smaller controllers"): antes de dividir a ciegas, revisar si el controller actual existe *a propósito* como consolidación de dos controllers previos que definían la misma ruta (route collision) — si es así, confirmar con el usuario antes de reabrir esa división (ver sección 3). Si se decide dividir: cada acción HTTP se mueve a su propia clase `[ApiController]` con el **mismo `[Route("api/v{version:apiVersion}/recurso")]`** en ambas — la ruta final de cada endpoint (`[HttpGet]`, `[HttpPost("accion")]`) no cambia, solo la organización en archivos; verificar que ningún verbo/ruta quede definido en más de una clase (causaría un error de arranque de ASP.NET Core por ruta duplicada). Los tests de integración que pegan por HTTP (`WebApplicationFactory`) no necesitan cambios — no referencian el nombre de la clase del controller. Ejemplo: `WarehousesController` → `WarehousesController` (GET) + `WarehousesValidationController` (POST validate), commit `92d44b6`.

## 7. Métrica de éxito

- Baja el número de issues abiertos en SonarQube.
- La cobertura de tests no disminuye.
- Cero regresiones funcionales (los tests que pasaban, siguen pasando).
