# SPC-Star SOP del técnico de línea

Actualizado el 21 de octubre de 2026.

## 1. Entrar e identificar el trabajo

Abra SPC-Star, introduzca su usuario/contraseña, seleccione su turno y pulse Sign In. Compruebe el usuario mostrado. Change password está en la pantalla inicial; pida a QA un restablecimiento si hace falta. No comparta credenciales ni deje su cuenta a otra persona.

Use el SOP del operador para el procedimiento completo. El técnico puede inspeccionar, corregir User Error autorizados y aprobar fallas con su propia cuenta. El acceso Line Tech no incluye Setup ni los informes de Data. No use otra cuenta para obtener funciones no disponibles. Los nombres de botones se conservan como aparecen en el programa.

## 2. Comprobar el trabajo

1. Confirme Job Number, Machine, Part Number, Operation e Inspection Phase según el trabajo asignado.
2. Pulse Load y compruebe el contexto sobre Inspection Items. Cada operación tiene sus propios requisitos.
3. Confirme el envío de la inspección inicial antes de avanzar de fase. Lea el bloqueo de fase en vez de evitarlo.
4. Solicite a QA revisar inspecciones o fallas anteriores cuando esos registros sean necesarios para investigar.

Los campos de pieza/trabajo sugieren registros relacionados. Si no aparece el trabajo, confirme números y acceso con QA; no seleccione un trabajo o fase ajeno para obtener una pantalla.

## 3. Confirmar datos y materiales

Revise datos requeridos y confirmación de Save Job Data. Compare Wire Shipment Number con la identificación del material. Los datos permanecen entre fases y sesiones hasta cambiarlos y guardarlos.

Revise cada fila y Current lot. Los lotes iniciales requeridos se guardan con Material issue at job start antes de inspeccionar. Para reemplazos, seleccione Material change, introduzca el lote en su fila y Save Material Lot. Compruebe Current lot después.

Filas separadas representan materiales o identidades de lote distintas aunque coincida el número de pieza. No combine lotes internos/de proveedor ni suponga que una fila cubre otra. Solicite a QA corregir un lote guardado incorrecto; no invente otra entrega para ocultarlo.

## 4. Ayudar con mediciones

Revise Machine Counter y su mensaje de vencimiento. Los elementos pueden tener frecuencias distintas. Tab/Enter sigue el orden mostrado. Introduzca Box # completo cuando corresponda; no modifique conteos para forzar inspecciones.

Use el contador real de la máquina; puede comenzar en cualquier número y no debe retroceder. Después de cambiar herramental, complete Setup y Startup requeridos antes de In Process. Los controles se acumulan desde el inicio de la corrida: con inicio en 100, controles de 2,000/4,000/8,000 corresponden en 2,100, 4,100 y 8,100, incluyendo en cada punto los grupos menores que también vencen. Una inspección tardía o Coil Change no desplaza el calendario. Una falla requiere repetición inmediata, como se explica abajo.

Siga orden, herramienta, unidades, límites y etiquetas de muestra/posición. Introduzca la lectura real con ceros finales. El avance automático espera un segundo después de los decimales esperados; otro dígito reinicia la pausa. Tab/Enter termina la entrada. Box # no tiene un número fijo de dígitos y termina con Tab/Enter.

Los atributos usan un Accept/Reject por lote, pero se revisa toda la muestra requerida. Cualquier pieza fallida requiere Reject.

Para indicadores, seleccione el campo antes de transmitir y compruebe la lectura completa sin prefijo añadido. Connect Gauge corresponde a medidores seriales configurados; seleccione el dispositivo y revise estado. Pida a QA revisar el perfil si el formato es incorrecto. No adivine velocidad serial ni reduzca precisión para continuar.

Para Hole Diameter por pasador, confirme el bloque asignado. Se guarda el diámetro correspondiente; pasador 7 falla. Para Removal calculado, revise Raw y Polished emparejados: Removal es Raw menos Polished. No reemplace el cálculo por un valor conveniente.

## 5. Interpretar OK, WATCH y falla

Verde OK refleja el resultado aceptado actual. Amarillo WATCH avisa de deriva o proximidad al límite. Lea la razón, revise Trend e investigue antes de producir piezas malas. WATCH no es bloqueo ni falla contada. Su recuperación depende de la regla; una lectura normal no siempre lo elimina.

Rojo identifica lectura fallida o atributo rechazado. El bloqueo indica elemento y violación. LSL/USL son límites de aceptación, no ajustes para solucionar el bloqueo. No cambie límites, cantidades ni reglas para ocultar un problema.

## 6. Liberar solamente un error de entrada

1. Para un error propio, el operador selecciona User Error, introduce sus credenciales y pulsa Clear Lock. No necesita acceso Line Tech para corregir su entrada.
2. Ayude si necesita confirmar la medición. Line Tech o QA autorizado puede liberar el error de otro usuario con sus propias credenciales.
3. User Error no requiere Cause details ni Action taken.
4. El operador corrige inmediatamente, comprueba el nuevo estado y continúa la misma inspección.

User Error no cuenta como falla ni evento separado de liberación. La inspección corregida todavía debe enviarse. Si la liberación o envío se rechaza, lea el mensaje; cerrar el panel no prueba aceptación del servidor.

Refresh Lock Status consulta el bloqueo del servidor. Reset Unlock Form borra campos del formulario, no el bloqueo. Ninguno evita una falla.

## 7. Terminar una inspección realmente fallida

1. Seleccione Machine, Tooling o Material según el problema real.
2. En Cause details describa la condición encontrada. No adivine; indique si la causa específica sigue desconocida.
3. En Action taken describa la respuesta real. Siga también el procedimiento de producto no conforme fuera de SPC-Star.
4. Introduzca su usuario/contraseña Line Tech para aprobar y pulse End Failed Inspection. Compruebe confirmación e inspección vacía del mismo contexto. Si el estado no es claro, solicite a QA verificar la falla guardada.
5. Resuelva el problema antes de comenzar otra inspección.

Estas categorías no permiten Clear Lock. Sin bloqueo activo, use End Inspection rojo y complete el formulario si realmente falló. No sirve para cancelar un borrador sin falla. Terminar una inspección fallida no autoriza liberar producto malo.

El Line Tech introduce detalles y aprueba la falla. Un operador solo puede hacerlo con acceso Line Tech aprobado por su supervisor y asignado a su cuenta. Después de resolver el problema, repita inmediatamente con el Machine Counter actual, incluso el mismo conteo. La repetición queda pendiente hasta completar una inspección aprobada; otros elementos conservan sus frecuencias.

## 8. Verificar envío y numeración

El operador completa lo requerido, pulsa Submit Inspection y comprueba confirmación de éxito. Si no se confirma, deténgase y solicite a QA revisar el estado guardado. Lecturas individuales no equivalen a inspección enviada.

La numeración continúa por trabajo, pieza, operación y fase entre sesiones. Cuentan envíos completados y fallas terminadas explícitamente; borradores no. Investigue aparentes duplicados antes de reemplazarlos. Después de una inspección por cantidad, revise el próximo conteo según la frecuencia de cada elemento.

## 9. Revisar la pantalla de inspección y documentar

Revise la inspección actual, mensaje de bloqueo y Current lot. Proporcione a QA los datos necesarios para investigar lecturas guardadas o corregir material. Line Tech no tiene acceso a los informes de Data.

Trend ofrece Individuals, Run Chart, Moving Range, Histogram y Control / Spec Limits para orden de lecturas, cambios adyacentes, distribución o límites configurados. La leyenda distingue normal, WATCH y medición fallida; un punto rojo no significa que fallaron todas las lecturas del trabajo.

Job Capability resume lecturas guardadas entre fases/máquinas para el trabajo/operación/variable. Revise cantidades y límites antes de concluir. Un índice vacío puede significar pocos datos, variación cero, especificación unilateral o límites incompatibles. Capacidad no autoriza aceptar una pieza mala.

Use el campo Note de inspección y Submit Note para ajustes, hallazgos y pendientes. Compruebe confirmación. Las notas permanecen asociadas al trabajo entre fases. Solicite a QA recuperar notas guardadas o exportar registros cuando haga falta.

Job History en Inspect muestra notas y respuestas. Al cargar nuevamente el historial, incluso después de enviar una inspección, una nota con respuesta nueva aparece al principio. La nota conserva autor y fecha; cada respuesta muestra su autor y fecha. No se actualiza automáticamente. Recargue el trabajo cuando no haya entradas en curso para revisar respuestas. Para responder desde la lista central de notas, solicite a QA.

## 10. Escalar y entregar el puesto

Avise a QA sobre fallas repetidas, requisitos incorrectos, datos faltantes, numeración inesperada, materiales incorrectos, errores del dispositivo o de envío y campos no editables. Indique trabajo, pieza, operación, fase, inspección, elemento, valor, mensaje y hora. No reconstruya registros ni adivine datos.

Después de entradas pendientes/sin conexión, solicite a QA verificar lo guardado al volver el servicio. Antes de cambiar equipo o entregar el puesto, revise confirmaciones e informe trabajo incompleto. Pulse Sign Out antes del siguiente usuario.

Actualizar el navegador o reiniciar normalmente el servidor conserva una sesión válida. Si aparece Sign In Required, introduzca su contraseña en ese cuadro; las entradas actuales permanecen en pantalla. Repita el guardado o envío interrumpido después de entrar. No se envía automáticamente. No actualice ni cierre sesión para quitar el cuadro y arriesgar trabajo sin guardar.
