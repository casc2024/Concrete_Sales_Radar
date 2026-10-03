# Concrete Sales Radar

Aplicación web **ASP.NET Core MVC (.NET 8)** con base de datos **PostgreSQL**, pensada para
prospección de obras de construcción y venta de concreto (Ready-Mix). Incluye landing page
de marketing, dashboard de oportunidades, registro de usuarios con **verificación por correo
(doble autenticación)**, membresías y panel de administración con roles.

---

## Funcionalidades

- **Landing page** (`/`) con hero, planes de precios dinámicos y llamadas a la acción.
- **Registro de usuario** (`/Cuenta/Registro`) con datos: Nombre, Apellido, Compañía, Correo y
  Teléfono + selección de plan de membresía. Validaciones de tipo de dato (correo con expresión
  regular, resto texto/número, máximo 50 caracteres) en cliente y servidor.
- **Doble autenticación por correo**: tras registrarse se envía un token de 6 dígitos al correo.
  El usuario debe verificarlo antes de poder iniciar sesión.
- **Login / logout** con cookies y contraseñas hasheadas (`PasswordHasher`).
- **Dashboard** (`/Dashboard`) — equivalente al prototipo *Concrete Sales Radar*: KPIs, pipeline
  por tipo de obra, prioridad comercial, búsqueda/filtros y CRUD de obras.
- **Administración** (`/Admin`, solo rol *Administrador*): mantenimiento de usuarios y de membresías.
- **Roles**: `Administrador` y `Usuario` (sembrados automáticamente).
- **Multilenguaje (i18n)**: interfaz en **Español, Inglés y Portugués** con selector de idioma
  en la barra superior. Se traducen vistas, etiquetas, mensajes de validación, mensajes del
  sistema y los correos. El idioma se guarda en una cookie de cultura. Los recursos están en
  `Resources/SharedResource.{resx,en.resx,pt.resx}`. *(El catálogo de planes y las obras de
  ejemplo se almacenan como datos en la base, en español.)*

## Modelo de datos (PostgreSQL)

| Tabla | Descripción |
|-------|-------------|
| `roles` | Roles de acceso (Administrador / Usuario). |
| `membresias` | Catálogo de planes (Starter, Professional, Business, Enterprise). |
| `usuarios` | Cuentas: datos básicos, hash de contraseña, rol, membresía, estado de verificación. |
| `codigos_verificacion` | Tokens de un solo uso enviados por correo (confirmación / 2FA). |
| `proyectos` | Obras prospectadas (dirección, tipo, presupuesto, yardas, contactos, prioridad). |

Las migraciones de EF Core se aplican automáticamente al iniciar la aplicación
(`DbSeeder.InicializarAsync`), que además siembra roles, planes, el usuario administrador y
obras de ejemplo.

## Ejecutar en local

```bash
cd ConcreteSalesRadar
dotnet run
```

La app aplica migraciones y seed contra la base configurada en `appsettings.json`
(`ConnectionStrings:Flota`). Acepta tanto formato URL (`postgresql://...`) como formato Npgsql.

### Usuario administrador por defecto

- **Correo:** `admin@concretesalesradar.com`
- **Contraseña:** `Admin123!`

> Cámbialos con las variables `Admin:Correo` / `Admin:Password` o editando el usuario desde el panel.

### Correo (SMTP) para la doble autenticación

Configura la sección `Email` en `appsettings.json` o por variables de entorno:

```json
"Email": {
  "Host": "smtp.tuproveedor.com",
  "Port": 587,
  "User": "usuario",
  "Password": "contraseña-o-app-password",
  "From": "no-reply@tudominio.com",
  "FromName": "Concrete Sales Radar",
  "UseStartTls": true
}
```

Si **no** se configura SMTP, la app funciona en *modo prueba*: no envía correos pero muestra el
código de verificación en pantalla, para poder probar el flujo completo.

## Pagos de membresía con Stripe (suscripción)

Las membresías se cobran como **suscripción mensual recurrente** con **Stripe Checkout**. En
`/Suscripcion` ("Mi membresía") el usuario elige un plan y se suscribe; al confirmarse el pago se
activa la membresía y se registra en la tabla `pagos`. El usuario puede **cancelar** (al final del
período). La activación es **idempotente** y ocurre tanto en el retorno de éxito como vía **webhook**
(fuente de verdad en producción), que maneja:

- `checkout.session.completed` → alta de la suscripción.
- `invoice.paid` → renovación mensual (extiende vigencia y registra el cobro).
- `customer.subscription.deleted` → fin de la suscripción (la membresía deja de estar pagada).

Configura la sección `Stripe` (en `appsettings.Development.json` local o por variables de entorno):

```json
"Stripe": {
  "SecretKey": "sk_test_...",
  "PublishableKey": "pk_test_...",
  "WebhookSecret": "whsec_...",
  "Moneda": "usd"
}
```

- Las claves se obtienen en el panel de Stripe (modo de prueba). Sin claves, la app funciona
  pero los botones de pago quedan deshabilitados con un aviso.
- **Webhook** (opcional pero recomendado en producción): crea un endpoint en Stripe apuntando a
  `https://TU-DOMINIO/Suscripcion/Webhook` con el evento `checkout.session.completed` y copia el
  signing secret en `Stripe:WebhookSecret`. En local puedes usar `stripe listen --forward-to
  localhost:5080/Suscripcion/Webhook`.
- Tarjeta de prueba: `4242 4242 4242 4242`, cualquier fecha futura y CVC.

## Despliegue en Railway

El proyecto incluye `Dockerfile` y `railway.json` (builder Dockerfile).

1. Crea un servicio en [Railway](https://railway.com/) apuntando a este repositorio.
2. Railway construirá con el `Dockerfile` y expondrá el puerto de la variable `PORT`
   (la app la lee en `Program.cs`).
3. Configura las variables de entorno:
   - `DATABASE_URL` → cadena de conexión de PostgreSQL (Railway la inyecta si enlazas el plugin de
     Postgres; también se acepta `ConnectionStrings__Flota`).
   - `Email__Host`, `Email__User`, `Email__Password`, `Email__From`, … para el envío de correos.
   - `Admin__Correo`, `Admin__Password` (opcional) para el administrador inicial.

## Nota de seguridad

- `appsettings.json` incluye una cadena de conexión y una contraseña de administrador de ejemplo
  para facilitar la puesta en marcha. **En producción usa variables de entorno** y **rota esas
  credenciales**; no las dejes versionadas en un repositorio público.
- Aviso `NU1902` (MailKit / BouncyCastle): advertencia transitiva de severidad moderada sin
  versión corregida disponible al momento; no afecta la compilación.
