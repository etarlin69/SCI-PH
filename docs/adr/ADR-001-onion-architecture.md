# ADR-001: Onion Architecture

- **Estado:** Aceptado
- **Fecha:** 2026-09-27

## Contexto

SCI-PH maneja datos clínicos, identidad, biometría, documentos e IA, y lo consumen tres clientes muy distintos (web, móvil y dispositivos ESP32). Varios integrantes trabajarán en paralelo.

Necesitamos:

- Que las reglas del dominio clínico no dependan de frameworks ni de proveedores externos.
- Poder cambiar o simular proveedores (Azure, OCR, IA, biometría) sin tocar el dominio.
- Límites claros para que cada rol trabaje sin pisar a otro.
- Poder probar el dominio y los casos de uso sin base de datos ni servicios externos.

## Decisión

Adoptamos **Onion Architecture** sobre **ASP.NET Core .NET 9**, con estos proyectos:

| Capa | Proyecto |
|---|---|
| Núcleo | `SCI-PH.Domain` |
| Aplicación | `SCI-PH.Application` |
| Infraestructura | `SCI-PH.Persistence`, `SCI-PH.Infrastructure` |
| Presentación / composition roots | `SCI-PH.Web.BFF`, `SCI-PH.Mobile.BFF`, `SCI-PH.Device.API` |

Las dependencias apuntan hacia el centro. Application define abstracciones (`Application/Abstractions`) y Persistence e Infrastructure las implementan. Las reglas completas están en [dependency-rules.md](../architecture/dependency-rules.md).

Cada capa expone un único método de registro (`AddApplication`, `AddPersistence`, `AddInfrastructure`).

## Consecuencias

**Positivas**
- El dominio se puede probar de forma aislada.
- Los proveedores externos se pueden reemplazar implementando la misma abstracción.
- Límites de responsabilidad claros entre roles del equipo.

**Negativas**
- Más proyectos y más indirección que una solución de un solo proyecto.
- Requiere disciplina: toda referencia nueva entre proyectos debe respetar las reglas.

**Pendiente**
- Decidir si se adopta una librería de mediación (por ejemplo, MediatR) o casos de uso como servicios simples. Se decidirá en un ADR aparte.
