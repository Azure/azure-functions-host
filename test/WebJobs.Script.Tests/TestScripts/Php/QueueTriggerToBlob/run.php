<?php
  $input = fgets(STDIN);
  $input = rtrim($input, "\n\r");
  fwrite(STDOUT, sprintf("PHP script processed queue message '$input'", $string));
  
  $output = fopen(getenv('output'), "w");
  fwrite($output, $input);
  fclose($output);
?>