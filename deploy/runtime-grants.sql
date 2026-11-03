-- Run only as the migration/database administrator after creating the roles.
-- The bootstrap login is temporary and must not be used by the long-running API.
GRANT CONNECT ON DATABASE elcarbonazo TO elcarbonazo_runtime, elcarbonazo_bootstrap;
GRANT USAGE ON SCHEMA public TO elcarbonazo_runtime, elcarbonazo_bootstrap;

GRANT SELECT ON public.usuario TO elcarbonazo_runtime;
GRANT UPDATE (contrasena_hash, intentos_fallidos, bloqueo_hasta)
    ON public.usuario TO elcarbonazo_runtime;
GRANT SELECT, UPDATE (estado, id_grupo) ON public.mesa TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE ON public.grupo_mesa TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.grupo_mesa_miembro TO elcarbonazo_runtime;
GRANT SELECT ON public.pedido TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE ON public.sesion_usuario TO elcarbonazo_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON public.token_refresh_sesion TO elcarbonazo_runtime;
GRANT SELECT, INSERT ON public.evento_auditoria TO elcarbonazo_runtime;

GRANT USAGE, SELECT ON SEQUENCE public.grupo_mesa_id_grupo_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.token_refresh_sesion_id_token_seq TO elcarbonazo_runtime;
GRANT USAGE, SELECT ON SEQUENCE public.evento_auditoria_id_evento_seq TO elcarbonazo_runtime;

GRANT SELECT, INSERT, UPDATE ON public.usuario TO elcarbonazo_bootstrap;
GRANT USAGE, SELECT ON SEQUENCE public.usuario_id_usuario_seq TO elcarbonazo_bootstrap;
GRANT SELECT, INSERT ON public.evento_auditoria TO elcarbonazo_bootstrap;
GRANT USAGE, SELECT ON SEQUENCE public.evento_auditoria_id_evento_seq TO elcarbonazo_bootstrap;
REVOKE ALL ON SCHEMA public FROM elcarbonazo_runtime, elcarbonazo_bootstrap;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO elcarbonazo_runtime, elcarbonazo_bootstrap;
REVOKE UPDATE, DELETE, TRUNCATE ON public.evento_auditoria FROM elcarbonazo_runtime, elcarbonazo_bootstrap;
