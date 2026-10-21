# SPC-Star SOP del operador

Actualizado el 21 de octubre de 2026.

Use este procedimiento para cargar un trabajo, registrar inspecciones, corregir errores de entrada e informar problemas.

## 1. Abrir SPC-Star e iniciar sesión

Abra la dirección de SPC-Star indicada para su área. La pantalla inicial ofrece Operator SOP, Line Tech SOP y QA SOP. Seleccione English o Spanish dentro del documento. Los nombres de botones se conservan como aparecen en el programa.

1. Introduzca su propio User y Password.
2. Seleccione el Shift que realmente está trabajando y pulse Sign In.
3. Compruebe su nombre arriba. Nunca registre inspecciones en la cuenta de otra persona ni comparta su contraseña.

Para cambiar la contraseña, seleccione Change password en la pantalla inicial. Introduzca usuario, contraseña actual y la nueva dos veces; pulse Save password. Si no puede entrar, solicite a QA un restablecimiento.

## 2. Seleccionar y cargar la inspección correcta

1. En Inspect, introduzca Job Number y seleccione la Machine que utiliza.
2. Introduzca o escanee Part Number. Las sugerencias de pieza y trabajo muestran registros relacionados. Si no aparece el trabajo esperado, confirme el número con el técnico; no elija otro trabajo.
3. Seleccione la Operation del trabajo realizado. Una operación distinta, como Test Polish, tiene sus propias inspecciones.
4. Seleccione Inspection Phase y pulse Load.
5. Verifique trabajo, máquina, pieza, operación y fase sobre Inspection Items antes de registrar resultados.

Complete y envíe la inspección inicial requerida antes de pasar a la siguiente fase. Si SPC-Star bloquea una fase, lea el mensaje y termine la inspección pendiente. Setup, Startup, In Process, Coil Change, Spool y End of Spool no son intercambiables. No todas las operaciones usan todas las fases. No seleccione una fase ajena para evitar el bloqueo.

## 3. Guardar datos del trabajo y lotes

Complete los campos específicos en Job Data. Pulse Save Job Data y compruebe la confirmación. Para Wire Shipment Number, compare el número con la identificación del material. Los datos permanecen entre fases e inicios de sesión hasta que se cambien y guarden.

1. En Materials, lea el nombre, número de pieza y descripción de cada fila.
2. Al comenzar el trabajo, introduzca cada lote requerido y seleccione Material issue at job start. Pulse Save Material Lot.
3. Compruebe Current lot en todas las filas requeridas. Los lotes iniciales deben estar guardados antes de comenzar la inspección.
4. Cuando cambie el material, introduzca el nuevo lote en su fila, seleccione Material change y pulse Save Material Lot. Revise Current lot nuevamente.

Cada fila requiere su propio dato, incluso los campos de lote interno y de proveedor de la misma pieza. Los lotes guardados siguen vigentes entre fases y sesiones hasta reemplazarlos. No vuelva a registrar un lote sin cambios en cada inspección. Pida al técnico o QA una corrección registrada si un lote guardado es incorrecto.

## 4. Introducir Machine Counter y Box #

Introduzca el Machine Counter completo. Lea el mensaje inmediatamente a su lado: indica si corresponde inspeccionar o el próximo conteo requerido. Tab o Enter continúa según el orden mostrado. Distintos elementos pueden tener frecuencias diferentes; siga sus mensajes.

Introduzca el Box # completo cuando corresponda. Use Tab o Enter al terminar. Son campos de conteo/identificación, no dimensiones. No cambie un conteo para forzar otro calendario de inspección.

Use el contador real de la máquina, no un conteo desde cero para el trabajo. El trabajo puede comenzar en cualquier número. El conteo no debe retroceder. Después de un cambio de herramental, realice Setup y Startup requeridos antes de continuar In Process.

Los controles por cantidad se acumulan desde el contador inicial de la corrida. Por ejemplo, con inicio en 100 y frecuencias de 2,000, 4,000 y 8,000 piezas: en 2,100 haga los controles de 2,000; en 4,100 los de 2,000 y 4,000; en 8,100 los tres grupos. Use las frecuencias indicadas para su pieza. Una inspección tardía o Coil Change no desplaza estos puntos. Para una falla, siga las instrucciones de repetición abajo.

## 5. Leer el elemento de inspección

Siga el orden mostrado. Lea nombre, requisito, herramienta, unidad, fase, tamaño de muestra y frecuencia. Compruebe las etiquetas de lados, patas, husillos u otras posiciones para registrar cada lectura en su campo.

LSL es la lectura mínima aceptable y USL la máxima. Un valor igual al límite pasa. Un requisito de mínimo solamente no tiene límite superior; uno de máximo solamente no tiene inferior. Un Target vacío no requiere que el operador lo complete. LCL/UCL, si aparecen, son límites de control del proceso, no sustitutos de los límites de especificación.

Si un requisito, límite, muestra o fase parece incorrecto, deténgase y consulte al técnico o QA. No invente límites ni escriba un número conveniente para continuar.

## 6. Introducir mediciones y atributos

1. Mida con la herramienta indicada e introduzca el valor real, incluidos decimales y ceros finales.
2. Si hay un prefijo decimal, complete esa lectura sin agregar otro prefijo. Los campos alimentados por dispositivo deben recibir la lectura completa sin un prefijo adicional.
3. Una dimensión avanza después de los decimales esperados y una pausa de un segundo. Otro dígito reinicia la pausa. Tab o Enter termina la entrada cuando sea necesario, incluso con menos dígitos. Revise el valor antes de continuar.
4. Para atributos, inspeccione la cantidad requerida y seleccione un Accept o Reject para el lote. Si falla cualquier pieza revisada, seleccione Reject. La selección avanza al siguiente campo.
5. Complete cada muestra y posición requerida. No copie lecturas a otras muestras salvo que sean sus resultados reales.

Para un indicador conectado como teclado, seleccione la muestra y transmita la lectura. Si aparece Connect Gauge para un medidor serial configurado, púlselo, elija el dispositivo asignado y compruebe su estado. Use Disconnect al terminar. Si el dato aparece en otro campo o unido a un prefijo, deténgase y pida ayuda; no cambie la configuración del dispositivo.

Cuando Hole Diameter use entrada de pasador, utilice el bloque asignado e introduzca el pasador usado. SPC-Star guarda su diámetro configurado. Los pasadores 1 a 6 pasan; el 7 falla. Cuando Removal sea calculado, introduzca Raw y Polished correspondientes y revise el resultado; no lo sobrescriba.

## 7. Interpretar la franja de color

Verde OK significa resultado actual aceptable. Amarillo WATCH indica deriva o proximidad a un límite según la regla configurada. Lea la razón y avise al técnico o QA. WATCH por sí solo no bloquea; vuelve a OK al cumplir la recuperación configurada.

Rojo significa lectura fallida o atributo rechazado. Una corrección se evalúa con el nuevo valor. Gris NEW o EDIT no confirma aceptación ni envío; lea el mensaje y complete o corrija la entrada.

## 8. Corregir un error de entrada bloqueado

1. Lea el mensaje para identificar elemento y valor fallido.
2. Para un error de entrada propio, no una pieza realmente mala, seleccione User Error usted mismo. Introduzca su usuario y contraseña y pulse Clear Lock. No necesita acceso Line Tech para corregir su propio error.
3. Corrija la lectura inmediatamente y continúe la misma inspección. User Error no cuenta como inspección fallida.

No use User Error para aceptar producto realmente fuera de especificación. Si el bloqueo permanece o no puede corregir, lea el mensaje y consulte al técnico o QA. No siga intentando enviar contra un bloqueo activo.

## 9. Registrar una inspección realmente fallida

1. Detenga la entrada y llame al Line Tech por una falla real.
2. El Line Tech selecciona Machine, Tooling o Material, completa Cause details y Action taken con hechos e introduce su usuario/contraseña para aprobar la falla.
3. El Line Tech pulsa End Failed Inspection. Compruebe confirmación e inspección vacía del mismo trabajo, operación y fase. Si el estado no es claro, solicite a QA revisar la falla guardada.
4. Un operador solo puede registrar los detalles y aprobar la falla si su supervisor autorizó acceso Line Tech y ese acceso está asignado a su cuenta. En caso contrario debe hacerlo el Line Tech.

Estas categorías no permiten Clear Lock. Resuelva el problema real y siga las instrucciones antes de comenzar de nuevo. Terminar una inspección fallida no autoriza liberar producto no conforme.

Después de resolver el problema, repita la inspección inmediatamente con el Machine Counter actual, aunque no haya cambiado. No espere el próximo conteo programado. Una falla no satisface el requisito; enviar correctamente la repetición restablece el calendario normal.

Si realmente falló pero no hay bloqueo abierto, End Inspection rojo abre el mismo formulario. El Line Tech introduce los detalles y aprueba como se indicó. Registra una falla; no sirve para cancelar o pausar.

## 10. Enviar y verificar

1. Revise Machine Counter, Box # cuando corresponda, datos/lotes guardados y todas las muestras.
2. Pulse Submit Inspection una vez y espere la confirmación de éxito.
3. Lea el mensaje de envío. Si muestra un error o no confirma éxito, deténgase y avise al técnico o QA.
4. Comience la siguiente solamente después de recibir confirmación de éxito.

La numeración continúa para el mismo trabajo, operación y fase al volver a entrar otro día. Campos verdes o lecturas en pantalla no significan que se envió. Si falla el envío, siga el mensaje o consulte al técnico/QA. No cree una inspección de reemplazo sin conocer el estado de la anterior.

## 11. Trend y notas

Trend muestra lecturas guardadas del trabajo para la variable seleccionada. Elija variable y tipo de gráfico para revisar cambios. Job Capability resume mediciones; no autoriza aceptar una lectura mala. Consulte al técnico o QA sobre tendencias preocupantes.

Use el campo Note de la pantalla de inspección y pulse Submit Note. Compruebe la confirmación. Las notas permanecen asociadas al trabajo entre fases y explican ajustes, reparaciones o problemas. Solicite a QA revisar registros guardados cuando haga falta. No sustituyen causa y acción en el formulario de falla.

Lea notas y respuestas en Job History de Inspect. Una nota con respuesta nueva pasa al principio cuando se carga nuevamente Job History, incluso después de enviar una inspección. La nota conserva autor y fecha; la respuesta muestra quién respondió y cuándo. Las respuestas no aparecen automáticamente mientras la pantalla permanece sin actualizar. Recargue el trabajo cuando no esté introduciendo una inspección para revisar respuestas. Los operadores no tienen acceso a las pestañas de informes de Data.

## 12. Relevo, conexión y salida

Antes de salir, confirme que recibió mensajes de éxito para inspecciones enviadas, cambios de material y notas. Informe trabajo incompleto o problemas pendientes. No suponga que una entrada sin terminar se transfirió a otro equipo o fue enviada; solicite a QA confirmar su estado antes de repetirla.

Si SPC-Star está sin conexión o indica entradas pendientes/rechazadas, deténgase y avise al técnico o QA. Al volver la conexión, solicite a QA confirmar lo guardado antes de continuar. Una entrada pendiente no confirma guardado.

Pulse Sign Out al terminar o entregar la estación. La siguiente persona debe usar su cuenta.

Actualizar el navegador o reiniciar normalmente el servidor conserva una sesión válida. Si aparece Sign In Required, introduzca su propia contraseña en ese cuadro. Las entradas actuales permanecen en pantalla. Después de entrar, repita el guardado o envío interrumpido; no se envía automáticamente. No actualice ni cierre sesión para quitar el cuadro, porque puede perder trabajo sin guardar.
