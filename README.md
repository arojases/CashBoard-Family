# CashBoard Family

Dashboard financiero familiar full stack para administrar ingresos, gastos, presupuestos, metas de ahorro y deudas. Diseñado como proyecto profesional de portafolio con una interfaz responsive y datos demo en CLP.

## Stack

- Angular 20 standalone + Signals + SCSS
- ASP.NET Core 8 Minimal API, JWT y Swagger
- Entity Framework Core + PostgreSQL 16
- Docker Compose

## Ejecutar el frontend

```bash
cd frontend
npm install
npm start
```

Abre `http://localhost:4200`. La demo visual funciona sin backend y permite navegar, buscar, filtrar, cambiar el tema y agregar movimientos durante la sesión.

## Ejecutar API y base de datos

Con Docker instalado:

```bash
docker compose up --build
```

Opcionalmente copia `.env.example` como `.env` y reemplaza sus valores. El archivo `.env` está ignorado por Git y nunca debe publicarse.

API: `http://localhost:8080` · Swagger: `http://localhost:8080/swagger` · PostgreSQL: puerto `5432`.

Para desarrollo local con .NET SDK 8, crea la migración inicial desde `backend/CashBoard.Api`: `dotnet ef migrations add InitialCreate` y luego `dotnet ef database update`.

## Arquitectura y permisos

La separación `frontend` / `backend` mantiene la UI desacoplada de la API REST. Todas las entidades pertenecen a una familia mediante `FamilyId`. JWT incorpora `familyId` y rol; los roles previstos son `Admin`, `Member` y `Guest`. Antes de producción se debe aplicar un filtro global por familia, políticas por rol, secretos por variables de entorno y refresh tokens.

## API principal

| Método | Ruta | Función |
|---|---|---|
| POST | `/api/auth/login` | Autenticación y JWT |
| GET | `/api/dashboard/summary` | Métricas del mes |
| GET/POST | `/api/transactions` | Movimientos |
| GET | `/api/budgets/current` | Presupuestos vigentes |
| GET/POST | `/api/savings-goals` | Metas de ahorro |
| GET | `/api/debts` | Deudas |

## Próximas fases

1. Añadir migraciones y seeder transaccional para usuarios/categorías demo.
2. Conectar servicios Angular a la API y agregar interceptor JWT y guards.
3. Completar CRUD, validación FluentValidation, aportes/pagos y filtros paginados.
4. Agregar pruebas xUnit/Jasmine, exportación PDF/Excel y CI/CD.
5. Desplegar Angular en Vercel/Netlify y API/PostgreSQL en Render, Railway o Azure.

No uses las credenciales ni la clave JWT de desarrollo en producción.
