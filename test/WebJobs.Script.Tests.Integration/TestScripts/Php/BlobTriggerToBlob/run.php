<?php
  $inputBlobTrigger = file_get_contents(getenv('inputBlobTrigger'));
  $inputBlobTrigger = rtrim($inputBlobTrigger, "\n\r");
  fwrite(STDOUT, sprintf("PHP script processed blobTrigger '$inputBlobTrigger'", $string));
  
  $inputBlob = file_get_contents(getenv('inputBlob'));
  $inputBlob = rtrim($inputBlob, "\n\r");
  fwrite(STDOUT, sprintf("PHP script processed blob '$inputBlob'", $string));
  
  $outputBlob = fopen(getenv('outputBlob'), "w");
  fwrite($outputBlob, $inputBlobTrigger."_".$inputBlob);
  fclose($outputBlob);
?>