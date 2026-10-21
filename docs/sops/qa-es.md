# SPC-Star SOP de QA

Actualizado el 21 de octubre de 2026.

## 1. Entrar y navegar

Entre con su usuario/contraseña y turno real. Compruebe el usuario mostrado. Change password está en la pantalla inicial; complete contraseñas actual/nueva y confirmación, luego Save password.

Inspect es la pantalla de producción. Setup ofrece Parts & Inspections, Users, Machines y Rules según permisos. Data ofrece Ledger, Part Quality, Charts y Top Issues. Use el SOP del operador para inspeccionar y el del técnico para resolver problemas. No tome credenciales prestadas. Los nombres de controles se conservan como aparecen en el programa.

QA puede exportar información. La importación de datos y configuración no está disponible para QA.

Admin ofrece Notes y Change History para QA. QA - Read only puede consultar y exportar Data, pero no editar registros, mantener configuración ni responder en Admin > Notes. Los pasos de mantenimiento y corrección siguientes requieren acceso QA regular.

## 2. Revisar con el operador

Confirme Job Number, Machine, Part Number, Operation e Inspection Phase; pulse Load. Complete la inspección inicial antes de avanzar de fase. No use otra operación/fase para evitar un bloqueo.

Compruebe confirmaciones de Save Job Data y Save Material Lot. Registre lotes iniciales con Material issue at job start y reemplazos con Material change. Datos y lotes permanecen entre fases/sesiones hasta cambiarlos y guardarlos. Distinga filas por nombre además del número, especialmente lotes internos/de proveedor. Revise Current lot después.

Machine Counter controla los mensajes por cantidad. Box # se escribe completo. Siga orden y posiciones de muestra. La precisión guardada de LSL/USL determina decimales esperados; el avance espera un segundo para más dígitos. Tab/Enter termina la entrada. Los indicadores deben transmitir el valor completo sin prefijo añadido.

Use el contador real de la máquina; puede comenzar en cualquier número y no debe retroceder. Setup y Startup requeridos establecen la corrida actual después de cambiar herramental. Los controles se acumulan desde ese inicio: con inicio en 100, los de 2,000 vencen en 2,100; en 4,100 se incluyen los de 2,000 y 4,000; en 8,100 los de 2,000, 4,000 y 8,000. Use las frecuencias configuradas para la pieza. Inspecciones tardías o adicionales y Coil Change no desplazan estos puntos.

Los atributos usan un Accept/Reject por lote sin reducir la muestra física. Removal calculado usa Raw menos Polished correspondientes. Hole Diameter por pasador guarda su diámetro según bloque asignado. Nunca cambie un requisito para aceptar una lectura incorrecta.

Al inspeccionar, complete los campos, pulse Submit Inspection y compruebe confirmación y Job History. La numeración continúa por trabajo, operación y fase entre sesiones. Lecturas en pantalla o solicitudes pendientes no prueban envío.

## 3. Corregir User Error o terminar una falla

1. Lea el mensaje y confirme medición o atributo fallido.
2. El operador libera su propio error seleccionando User Error e introduciendo sus credenciales en Clear Lock. No necesita acceso Line Tech. Line Tech o QA puede ayudar con el error de otro usuario. Corrija inmediatamente y continúe la misma inspección. No requiere causa/acción ni cuenta como falla o liberación separada.
3. Para falla real, el Line Tech selecciona Machine, Tooling o Material, completa Cause details y Action taken y aprueba con sus credenciales antes de End Failed Inspection. QA también puede aprobar. Un operador solo puede hacerlo con acceso Line Tech aprobado por su supervisor y asignado a su cuenta. No permiten Clear Lock.
4. Compruebe registro rojo, lecturas/motivo conservados e inspección vacía del mismo trabajo/operación/fase.

End Inspection abre el formulario sin bloqueo activo; no cancela borradores. Siga el procedimiento separado de producto no conforme; el programa no autoriza liberar producto. Refresh Lock Status consulta al servidor; Reset Unlock Form borra el formulario, no el bloqueo. Investigue cualquier rechazo.

Una inspección fallida queda pendiente para repetir inmediatamente al resolver el problema. Use el Machine Counter actual aunque no cambió; no espere el próximo conteo. Una repetición aprobada restablece el calendario normal. Los elementos ajenos a la falla mantienen sus propias frecuencias.

## 4. Mantener Parts & Inspections

1. Abra Setup > Parts & Inspections. Seleccione la pieza existente en Edit Existing Part y Load Part antes de editar.
2. Revise Part number, Part description, Product group y Operation. Mantenga Blank Code/Hole Size cuando correspondan. Productos terminados, blancos y materiales requieren sus descripciones correctas.
3. Use Job Data Fields para información persistente requerida. Mantenga nombres, requisitos e instrucciones del operador para esa pieza.
4. En Materials, mantenga etiqueta, número, descripción y Required por entrada. Separe materiales e identidades de lote; no combine números distintos en una fila.
5. En Inspection Items, mantenga nombre, tipo Variable/Attribute, Requirement / context, Tool y Unit. Conserve instrucciones de origen. Configure fases, muestras físicas, posiciones, orden y frecuencia reales de esa operación.
6. Copie LSL/USL de la hoja vigente aprobada y requisitos confirmados, con ceros finales. Deje vacíos targets o límites ausentes. No calcule puntos medios ni copie especificaciones a LCL/UCL. Solo use límites de control presentes en origen.
7. Seleccione regla de deriva, pulse Save Part Inspection, lea confirmación, recargue y verifique la pantalla del operador.

Verifique cambios contra la fuente vigente aprobada, no una exportación o respaldo antiguo. Export Parts & Inspections genera una copia; QA no importa configuración.

Un mínimo pasa a partir de LSL inclusive; un máximo hasta USL inclusive; ambos extremos de un rango pasan. Mantenga precisión en los límites sin trucos adicionales. Clear reinicia el formulario sin guardar.

Siga hojas y decisiones operativas confirmadas para fases. Test Polish es una operación separada. Lados/posiciones requieren lecturas propias. Una selección por lote no reduce la muestra física del atributo.

## 5. Mantener Users y Machines

En Users, expanda el grupo del nivel de acceso y seleccione una cuenta, o New User. Introduzca usuario, nivel disponible, turno y grupos permitidos. Save User y verifique. Asigne solamente niveles ofrecidos a su cuenta. Reset Password afecta al usuario seleccionado; comuníquelo solo a esa persona. Delete User es mantenimiento intencional, no corrección de historia. No cree cuentas compartidas ni conceda acceso ajeno al trabajo/capacitación. Export Users genera referencia; importar masivamente no es función de QA.

Asigne acceso Line Tech a un operador solamente con aprobación de su supervisor. Use su cuenta personal; no comparta un usuario Line Tech para aprobar fallas.

En Machines, seleccione registro o New Machine. Revise Machine ID, Description y Allowed product groups. Sin restricciones de grupo permite todos. Save Machine y compruebe la lista del operador. Seleccione Keyboard input o Serial text gauge según equipo y la velocidad serial indicada por el dispositivo. Export Machines genera referencia. Delete elimina una máquina, no el historial del trabajo.

## 6. Seleccionar regla de deriva

En Setup > Rules, seleccione Global default rule set y Save Global Rule. Los elementos Use Global Default heredan; una selección explícita la reemplaza. Revise Details antes de elegir. Un cambio global puede afectar muchas piezas.

Spec Limit Only evalúa especificaciones sin patrones de deriva. No Automatic Rule desactiva controles automáticos de variables medidas y no debe seleccionarse sin revisión; atributos rechazados todavía bloquean. Otras familias usan sus patrones/requisitos mostrados. No invente límites de control para una regla.

## 7. Configurar Custom Rule

Pulse Configure en Custom Rule. Defina nombre y controles, revise resumen y pulse Save Custom Protocol:

1. Enable custom Watch rules activa avisos. Desactivarlo no elimina la evaluación individual fuera de especificación de un elemento con Custom Rule.
2. Directional Drift usa un promedio por variable e inspección completada. Seleccione cantidad consecutiva y dirección superior, inferior o ambas. El primer promedio inicia comparación; los siguientes deben avanzar en esa dirección. Elija si promedios iguales o inversión reinician el conteo.
3. Near Specification Limits revisa muestras individuales en zona de aviso. Use porcentaje del rango LSL-USL o distancia exacta dentro del límite. Una especificación unilateral necesita distancia exacta, no porcentaje de un rango inexistente.
4. Repeated Near-Limit Inspections define cuántas de las últimas inspecciones completadas contienen una muestra en zona. Superior/inferior cuentan por separado.
5. Show Watch when usa Any o All condiciones activas. Con All se evalúa la combinación al enviar.
6. Normal completed inspections before OK define recuperación. Deben despejarse todas las condiciones; un nuevo aviso reinicia recuperación.
7. Añada notas y confirme que el elemento usa Custom Rule o hereda una regla global Custom.

Lados/husillos permanecen separados. Inspecciones incompletas/fallidas no cuentan como promedios completados; muestra uno usa su lectura. WATCH es amarillo y orientativo; valores individuales fuera de límites bloquean con Custom Rule. Recupera OK según configuración. Las mediciones con aviso aparecen amarillas en Ledger/export; no se añade evento WATCH separado a Job History.

## 8. Data > Ledger y correcciones

Ledger, Part Quality, Charts y Top Issues usan los mismos filtros: Part, Job, Operation, Phase, Machine, Shift, Variable, From y To. La selección se conserva entre estas pestañas. Los campos opcionales vacíos y las opciones All incluyen todos los valores coincidentes. Ejecute la vista seleccionada después de cambiar filtros; Export XLSX usa la selección de Ledger.

1. Abra Data > Ledger. Introduzca pieza, trabajo o ambos. Las sugerencias relacionan registros coincidentes.
2. Filtre Operation, Phase, Shift y fechas; pulse Review Job. All shifts incluye todos los turnos. Shift filtra el historial, los resúmenes de capacidad y Export XLSX. Solo pieza incluye sus trabajos; trabajo seleccionado muestra Job Capability e Inspection / Job History.
3. Abra Details para lecturas y contexto, no solo Machine Counter. Revise usuario, hora, operación, fase y muestras.
4. En fallas rojas revise valor/atributo, violación, categoría, Cause details original y Action taken. User Error y WATCH aislado no son fallas reales.
5. Use edición autorizada para lecturas/Machine Counter, o Edit Lot para material. Verifique cambio. No cree otra inspección ni enlaces supuestos para reparar datos viejos.
6. Export XLSX usa la revisión seleccionada. Compruebe filtros; Job Notes incluye notas dentro del trabajo/fechas seleccionados, también de otras fases.

La numeración continúa por trabajo, pieza, operación y fase. Solo cuentan envíos completados y fallas terminadas explícitamente. Lecturas sin envío no generan completados. Una falla parcial contiene solo lecturas tomadas. Escale enlaces ausentes; no reconstruya por hora.

## 9. Data > Part Quality

Introduzca Part y opcional Job, Operation, Phase, Machine, Shift y fechas; Load Summary. Revise totales finalizados y variables en orden. Find variable filtra tabla.

Seleccione fila o Variable para sus fallas. Revise fase, lecturas, violación, categoría, detalles, acción, notas, trabajo, máquina, contador y operador. Previous/Next muestra más fallas de esa variable, no todas las del sistema.

El porcentaje de aprobación de pieza usa inspecciones finalizadas aprobadas dividido entre finalizadas, no desperdicio. Las variables cuentan inspecciones que realmente las revisaron, no campos ni toda la producción. Variables no alcanzadas en falla parcial no cuentan inspeccionadas. Avisos de datos faltantes requieren investigar, no inventar lecturas o motivos.

## 10. Charts y Job Capability

En Data > Charts elija pieza/trabajo y filtros disponibles de operación, máquina, variable, fase y fechas. Seleccione gráfico y Run Report. Revise fase específica frente a All phases. Variables siguen orden configurado.

Individuals y Run Chart muestran lecturas ordenadas. Moving Range muestra cambios adyacentes; Histogram distribución; Control / Spec Limits límites. La leyenda indica normal, WATCH amarillo y medición fallida roja. Un punto rojo no significa falla de todas las lecturas.

Job Capability muestra mínimo, máximo, promedio, desviación e índices Cp/Cpk/Pp/Ppk disponibles. Panel de inspección y Ledger incluyen lecturas entre fases/máquinas del mismo trabajo/operación/variable. Fallas reales permanecen; borradores sin envío no son datos completados. Compare filtros y límites iguales.

Cp/Cpk usan variación interna; Pp/Ppk variación global. Límites ausentes, pocos datos, variación cero o límites distintos por fase pueden dejar índices vacíos. Una especificación unilateral permite Cpk/Ppk, no Cp/Pp bilaterales. Índices no son veredicto global ni autorización de liberar producto; revise estabilidad y proceso.

## 11. Top Issues y exportaciones

En Data > Top Issues seleccione filtros disponibles de pieza/trabajo/máquina/turno/variable/fechas y cantidad, luego Run Top Issues. Úselo para eventos fuera de especificación o atributos rechazados repetidos. Excluye Custom WATCH y correcciones User Error. Revise motivos en Ledger o Part Quality.

Ledger > Export XLSX contiene detalles de las inspecciones y Job Notes.

## 12. Leer y responder notas del trabajo

1. Abra Admin > Notes. Search busca texto de notas o respuestas. Part, Job, Machine, Posted by y Replied by limitan la lista. Pulse Show Notes después de cambiar filtros; Clear Filters los borra.
2. From y To filtran la fecha de la nota original, no la respuesta. Déjelos vacíos para buscar una respuesta reciente a una nota antigua. Previous/Next muestra más notas.
3. Lea nota y respuestas. Pulse Reply, escriba y pulse Send Response. Compruebe la respuesta guardada con su nombre y fecha. El máximo es 1,000 caracteres.
4. Autor, nota y fecha originales no cambian. Aquí no se editan ni borran respuestas. El operador ve la respuesta bajo la nota en Inspect > Job History al cargar nuevamente el historial, incluso después de enviar una inspección. No aparece automáticamente en una pantalla sin actualizar.

Las notas no reemplazan Cause details ni Action taken de la falla. Responder no libera un bloqueo, corrige una lectura ni completa una inspección.

## 13. Revisar Change History

Abra Admin > Change History. Filtre Search, Changed by, Area, Part, From o To y pulse Show Changes. Revise quién guardó, cuándo, qué cambió y los valores anteriores/nuevos disponibles. Previous/Next muestra más entradas; Export CSV exporta resultados seleccionados. Las importaciones aparecen resumidas. La vista es de solo lectura y no deshace cambios. Ediciones sin guardar no se registran.

## 14. Guardar, escalar y salir

Lea confirmaciones. Informe problemas con trabajo, pieza, operación, fase, inspección, elemento, valor real, hora y mensaje. Lecturas pendientes/sin conexión no son inspecciones enviadas. No borre historia, invente datos ni cambie límites para evitar fallas.

Antes de entregar, guarde notas/correcciones e informe problemas pendientes. Pulse Sign Out al terminar.

Actualizar el navegador o reiniciar normalmente el servidor conserva una sesión válida. Si aparece Sign In Required, introduzca su contraseña en ese cuadro. Las entradas actuales permanecen en pantalla. Repita la acción interrumpida después de entrar; no se repite automáticamente un guardado o envío fallido. No actualice ni cierre sesión para quitar el cuadro y arriesgar trabajo sin guardar.
