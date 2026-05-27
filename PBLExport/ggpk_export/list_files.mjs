// Список всех файлов в bundle index, фильтр по паттерну
import { Index } from 'pathofexile-dat/dat/fs/index.js';
import { createReadStream } from 'fs';
import path from 'path';

// Не работает напрямую — используем pathofexile-dat CLI для листинга
// Этот скрипт просто для справки
console.log('Use pathofexile-dat CLI with a files config entry instead');
