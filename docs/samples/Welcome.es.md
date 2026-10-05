# Bienvenido a 汗青

Abre un archivo Markdown, lee con comodidad y pulsa **Ctrl+E** cuando quieras editarlo.

En el modo de vista previa, la columna izquierda muestra los encabezados H1–H6. Haz clic en uno para ir a esa sección; al desplazarte, también se resalta la sección actual. En el modo de edición, el texto original aparece a la izquierda y la vista previa en tiempo real a la derecha, mientras el índice permanece oculto. Arrastra el separador para ajustar el ancho de las columnas.

> Tus documentos permanecen en tu equipo. Hanqing no requiere una cuenta.

## Primeros pasos

| Acción | Atajo |
| --- | --- |
| Abrir un documento | Ctrl+O |
| Crear un documento | Ctrl+N |
| Cambiar entre vista previa y edición | Ctrl+E |
| Guardar | Ctrl+S |
| Buscar texto | Ctrl+F |
| Ir al siguiente resultado de búsqueda | F3 |
| Cerrar la pestaña actual | Ctrl+W |
| Leer a pantalla completa | F11 |

La barra de herramientas izquierda permite crear documentos o ventanas, guardar una copia y abrir documentos recientes. También incluye controles de fuente y tema; el idioma y el tamaño de la ventana se encuentran en Más. Haz clic en el botón superior de la barra para mostrar nombres, descripciones y atajos, y vuelve a hacer clic para contraerla. Pasar el puntero por encima no cambia su estado.

La opción Índice de capítulos, dentro de Más, permite seleccionar los niveles H1–H6 tanto para la vista previa Markdown como para la exportación a PDF. La opción Conservar los archivos abiertos al salir está desactivada de forma predeterminada. Actívala para abrir de nuevo esos documentos al iniciar la aplicación; los cambios sin guardar siguen requiriendo confirmación.

La interfaz, la vista previa de lectura y el editor tienen ajustes de fuente independientes. Cada uno dispone de su propia fuente y tamaño; los cambios se aplican de inmediato y se recuerdan.

El modo de edición muestra una barra de formato encima del documento. Elige una fuente para el editor —automática monoespaciada, tipografía tradicional o una fuente instalada—, introduce un tamaño de 8 a 72 o usa **− / +**. Estas preferencias afectan a la visualización del editor y se recuerdan sin escribirse en el archivo Markdown. La barra se oculta en el modo de vista previa o cuando se cierran todos los documentos.

Las instalaciones nuevas usan Bambú claro, que combina una interfaz de bambú con un fondo de papel. Ambos temas de bambú muestran fibras de papel. Si ya tienes un tema elegido, se conserva. Elige un tema en Más → General → Tema; el botón de la barra lateral alterna entre Claro, Oscuro, Bambú claro y Bambú oscuro.

En los temas de bambú, las fuentes automáticas de la interfaz y de la vista previa usan tipografía tradicional. Para el chino tradicional se prefiere una fuente BiauKai instalada. También puedes elegir la tipografía tradicional directamente. Se conservan las fuentes elegidas de forma explícita, y el editor usa una fuente monoespaciada de forma predeterminada.

El tamaño de fuente predeterminado es 15 para la interfaz y 16 para la vista previa y el editor. La barra de herramientas aparece expandida al principio. El guardado automático está activado de forma predeterminada: los documentos que ya tienen una ubicación se guardan tres segundos después de dejar de escribir. En un documento nuevo, todavía tienes que usar **Ctrl+S** para elegir dónde guardarlo por primera vez.

## Exportar PDF

Haz clic en Exportar PDF, debajo de Guardar en la barra izquierda. Hanqing guarda primero el archivo Markdown y después crea un PDF en la misma carpeta con una marca de fecha y hora local, por ejemplo `Notes.md` → `Notes_20260922_153012.pdf` (`yyyyMMdd_HHmmss`).

Un documento nuevo pide primero una ubicación para guardar el archivo Markdown. Si cancelas o el guardado falla, la exportación se detiene. Se solicita confirmación antes de sobrescribir un PDF existente. El PDF usa páginas A4 blancas y la fuente y el tamaño actuales de la vista previa, e incluye todo el contenido y las imágenes locales.

## Listas de tareas

Este es un ejemplo de lista de tareas Markdown. Cuando no hay ningún documento abierto, esta introducción es de solo lectura. Abre o crea un documento antes de probar a marcar y guardar tareas en él.

- [x] Abrir un documento Markdown
- [ ] Marcar una tarea en tu documento y observar el indicador de cambios sin guardar de la pestaña
- [ ] Pulsar Ctrl+S para guardar los cambios

## Código

```csharp
var message = "Read. Edit. Save.";
Console.WriteLine(message);
```

Usa el botón situado en la esquina superior derecha de un bloque de código para copiarlo. También puedes seleccionar texto normal y pulsar **Ctrl+Shift+C** para copiarlo como Markdown.

## Consejos de edición

1. Usa el menú de párrafo de la barra de formato para elegir texto normal o encabezados **H1–H6**.
2. Selecciona texto para aplicar negrita, cursiva, tachado, código en línea o un enlace. **Ctrl+B** y **Ctrl+I** también aplican negrita y cursiva. Estas acciones insertan Markdown estándar y se pueden deshacer con **Ctrl+Z**.
3. Pega una URL sobre el texto seleccionado para crear un enlace Markdown.
4. Pega una imagen en el modo de edición para guardarla en una carpeta `images` junto al documento.
5. Haz clic con el botón derecho en la vista previa y elige Editar aquí para ir al párrafo correspondiente.

Los documentos de solo lectura siguen protegidos frente a los cambios mediante los botones de formato.

En el modo de edición, expande el panel de búsqueda para reemplazar texto. **Reemplazar todo solicita confirmación antes de continuar.**

---

[Volver al inicio](#bienvenido-a-汗青)
