CREATE OR REPLACE FUNCTION public.exportar_xml_nfe(
    data_inicial date,
    data_final date,
    caminho text)
RETURNS integer AS
$BODY$
DECLARE
    caminho_arquivo TEXT := caminho;
    nome_arquivo TEXT;
    comando_sql TEXT;
    contador INTEGER := 0;
BEGIN
    -- Exportar registros autorizados (somente c_serv = 'NFE')
    FOR nome_arquivo IN
        SELECT c_nfechave
        FROM a_nfeinf
        WHERE c_dataenv BETWEEN data_inicial AND data_final
          AND c_sit IN ('A', 'AT')
    LOOP
        comando_sql := 'COPY (
            SELECT c_xmlnfe
            FROM a_nfeinf
            WHERE c_nfechave = ''' || nome_arquivo || '''
              AND c_serv = ''NFE''
        ) TO ''' || caminho_arquivo || nome_arquivo || '-nfe.xml''';

        EXECUTE comando_sql;
        contador := contador + 1;
    END LOOP;

    -- Exportar registros cancelados
    FOR nome_arquivo IN
        SELECT c_nfechave
        FROM a_nfeinf
        WHERE c_dataenv BETWEEN data_inicial AND data_final
          AND c_sit IN ('C', 'CA')
    LOOP
        -- Exportar XML de autorizacao (somente NFE)
        comando_sql := 'COPY (
            SELECT c_xmlnfe
            FROM a_nfeinf
            WHERE c_nfechave = ''' || nome_arquivo || '''
              AND c_serv = ''NFE''
        ) TO ''' || caminho_arquivo || nome_arquivo || '-nfe.xml''';

        EXECUTE comando_sql;
        contador := contador + 1;

        -- Exportar XML de cancelamento (somente CAN)
        comando_sql := 'COPY (
            SELECT c_xmlnfe
            FROM a_nfeinf
            WHERE c_nfechave = ''' || nome_arquivo || '''
              AND c_serv = ''CAN''
        ) TO ''' || caminho_arquivo || nome_arquivo || '-can.xml''';

        EXECUTE comando_sql;
        contador := contador + 1;
    END LOOP;

    RETURN contador;
END;
$BODY$
LANGUAGE plpgsql;
