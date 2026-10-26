-- Additive, stable import location. Old rows retain their download-ID directory.
ALTER TABLE downloads ADD COLUMN import_directory TEXT;
CREATE UNIQUE INDEX ix_download_import_directory ON downloads(imported_library_id,import_directory)
WHERE imported_library_id IS NOT NULL AND import_directory IS NOT NULL;
