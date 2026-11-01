import React, { useEffect } from "react";
import { ButtonItem, callable, definePlugin, Logger } from "@steambrew/sdk";

const logger = new Logger("Raiol Tools");
const logBackendEvent = callable<[params: { message: string }], string>(
  "raiol_tools",
  "log_event",
);

async function sendLog(message: string): Promise<void> {
  try {
    await logBackendEvent({ message });
  } catch (error) {
    logger.error("Falha ao enviar evento ao backend do Millennium:", error);
  }
}

function RaiolToolsPanel(): React.ReactElement {
  useEffect(() => {
    logger.log("Painel visual inicializado.");
    void sendLog("Painel visual inicializado.");
  }, []);

  const handleClick = async (): Promise<void> => {
    logger.log("Botão de diagnóstico acionado.");
    await sendLog("Botão de diagnóstico acionado.");
  };

  return (
    <div style={{ padding: "16px" }}>
      <h2>Raiol Tools</h2>
      <p>Painel de demonstração. Os eventos são enviados pelo IPC interno do Millennium.</p>
      <ButtonItem
        label="Registrar evento de diagnóstico"
        onClick={() => void handleClick()}
      />
    </div>
  );
}

export default definePlugin(() => {
  logger.log("Plugin carregado.");

  return {
    icon: <span aria-label="Raiol Tools">RT</span>,
    content: <RaiolToolsPanel />,
    onDismount: () => logger.log("Painel visual desmontado."),
  };
});
