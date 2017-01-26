<?php
  $json = '[{"a":"b"}]';
  $res = getenv('res');
  file_put_contents($res, $json);
?>
