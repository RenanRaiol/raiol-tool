local logger = require("logger")
local millennium = require("millennium")

function log_event(message)
    if type(message) ~= "string" then
        error("message precisa ser uma string")
    end

    if #message == 0 or #message > 160 then
        error("message precisa ter entre 1 e 160 caracteres")
    end

    logger:info("[Raiol Tools] " .. message)
    return "logged"
end

return {
    on_load = function()
        logger:info("[Raiol Tools] Backend inicializado")
        millennium.ready()
    end,
    on_frontend_loaded = function()
        logger:info("[Raiol Tools] Frontend conectado pelo IPC do Millennium")
    end,
    on_unload = function()
        logger:info("[Raiol Tools] Backend encerrado")
    end,
}
