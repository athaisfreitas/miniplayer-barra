# Rodinha do mouse e foco da janela (2026-11-19)

## 1. A rodinha não chegava ao player
- **Sintoma:** girar a rodinha sobre o player não fazia nada (`OnMouseWheel` nunca disparava).
- **Causa:** o player é janela filha da barra de tarefas, que é de outro processo (Explorer); o Windows não entrega a rodinha a ele.
- **Solução:** `Ui/WheelHook.cs`, hook de mouse de baixo nível (`WH_MOUSE_LL`) ligado só enquanto o mouse está sobre o player; engole o evento e repassa o delta.
- **Lição:** em janela filha da barra de tarefas, não dependa de `OnMouseWheel`; use o hook.

## 2. "Abrir fonte" só piscava o ícone (YouTube Music)
- **Sintoma:** com a janela do app em segundo plano e não minimizada, o primeiro clique só piscava o ícone na barra. Minimizada, funcionava.
- **Causa:** o Windows recusa `SetForegroundWindow` de quem não é dono do foco; o caminho "restaurar" (minimizada) contorna isso.
- **Solução:** `SourceWindow.ForceForeground` (`AttachThreadInput` à thread da janela na frente + `BringWindowToTop`) como segunda tentativa.
- **Lição:** sempre ter o plano B de foco; testar com a janela em segundo plano **e** minimizada.
