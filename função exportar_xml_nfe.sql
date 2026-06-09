CREATE OR REPLACE FUNCTION exportar_xml_nfe(
    data_inicial date,
    data_final date,
    caminho text)
  RETURNS integer AS
$BODY$
DECLARE
    caminho_arquivo TEXT := caminho;
    nome_arquivo TEXT;
    comando_sql TEXT;
    contador INTEGER := 0;  -- Contador de arquivos exportados
BEGIN
    -- Exportar registros com c_sit = 'A' ou 'AT'
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
        ) TO ''' || caminho_arquivo || nome_arquivo || '-nfe' || '.xml''';
        EXECUTE comando_sql;
        contador := contador + 1;  -- Incrementa o contador
    END LOOP;

    -- Exportar registros com c_sit = 'C' ou 'CA'
    FOR nome_arquivo IN
        SELECT c_nfechave
        FROM a_nfeinf
        WHERE c_dataenv BETWEEN data_inicial AND data_final
          AND c_sit IN ('C', 'CA')
    LOOP
        -- Exportar o registro original
        comando_sql := 'COPY (
            SELECT c_xmlnfe
            FROM a_nfeinf
            WHERE c_nfechave = ''' || nome_arquivo || '''
        ) TO ''' || caminho_arquivo || nome_arquivo || '-nfe' || '.xml''';
        EXECUTE comando_sql;
        contador := contador + 1;

        -- Exportar o registro CAN (se existir)
        comando_sql := 'COPY (
            SELECT c_xmlnfe
            FROM a_nfeinf
            WHERE c_nfechave = ''' || nome_arquivo || '''
              AND c_serv = ''CAN''
        ) TO ''' || caminho_arquivo || nome_arquivo || '-can' || '.xml''';
        EXECUTE comando_sql;
        contador := contador + 1;  -- Incrementa mesmo se não houver registro (COPY não gera erro se não encontrar dados)
    END LOOP;
    
    RETURN contador;  -- Retorna o total de arquivos exportados
END;
$BODY$
  LANGUAGE plpgsql;
ALTER FUNCTION exportar_xml_nfe(date, date, text)
  OWNER TO icomp;
