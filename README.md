# CashBoard Family

Dashboard financiero familiar full stack para administrar ingresos, gastos, presupuestos, metas de ahorro y deudas. Diseñado como proyecto profesional de portafolio con una interfaz responsive y datos demo en CLP.

## Stack

- Angular 20 standalone + Signals + SCSS
- ASP.NET Core 10 Minimal API, JWT y Swagger
- Entity Framework Core + SQLite local / PostgreSQL 16 en producción
- Docker Compose

## Ejecutar la aplicación

```bash
cd frontend
npm install
npm start
```

`npm start` levanta automáticamente la API y Angular en la misma terminal, y abre CashBoard en `http://localhost:4300`. Para detener todo presiona `Ctrl+C`.

## MVP funcional local

También puedes iniciar cada servicio por separado para depuración. API (requiere .NET SDK 10):

```bash
cd backend/CashBoard.Api
dotnet run
```

Frontend Angular:

```bash
cd frontend
npm start
```

Cuentas de prueba: Administrador `admin@cashboard.cl` / `Admin1234!`; Visita `visita@cashboard.cl` / `Visita1234!`. La API crea automáticamente `cashboard-demo.db` sin registros financieros precargados. La base SQLite local está ignorada por Git.

El administrador puede gestionar usuarios desde **Configuración**: crear cuentas, cambiar datos o roles y eliminar usuarios. La cuenta Visita no ve Configuración y todas sus operaciones de escritura son rechazadas por la API.

Desde la misma pantalla el administrador puede personalizar el nombre del plan familiar. El modo nocturno y el menú de cierre de sesión están disponibles en la barra lateral.

## Ejecutar API y base de datos

Con Docker instalado:

```bash
docker compose up --build
```

Opcionalmente copia `.env.example` como `.env` y reemplaza sus valores. El archivo `.env` está ignorado por Git y nunca debe publicarse.

API: `http://localhost:8080` · Swagger: `http://localhost:8080/swagger` · PostgreSQL: puerto `5432`.

Para desarrollo local usa .NET SDK 10. SQLite se crea y carga automáticamente; Docker utiliza PostgreSQL mediante `DatabaseProvider=Postgres`.

## Arquitectura y permisos

La separación `frontend` / `backend` mantiene la UI desacoplada de la API REST. Todas las entidades se filtran por el `FamilyId` incorporado en el JWT. `Admin` dispone de CRUD completo; `Visitor` puede consultar dashboard y módulos, pero la API rechaza sus mutaciones con HTTP 403. Antes de producción deben configurarse secretos por variables de entorno y refresh tokens.

## API principal

| Método | Ruta | Función |
|---|---|---|
| POST | `/api/auth/login` | Autenticación y JWT |
| GET | `/api/dashboard/summary` | Métricas del mes |
| GET/POST | `/api/transactions` | Movimientos |
| DELETE | `/api/transactions/{id}` | Eliminar un movimiento propio de la familia |
| GET | `/api/categories` | Categorías familiares |
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
